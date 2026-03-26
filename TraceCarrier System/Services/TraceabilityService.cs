using System.Data;
using Npgsql;
using TraceCarrier_System.Contracts;
using TraceCarrier_System.Models;

namespace TraceCarrier_System.Services;

public sealed class TraceabilityService : ITraceabilityService
{
    private readonly string _connectionString;

    private static readonly object SchemaSync = new();
    private static bool _schemaInitialized;

    public TraceabilityService(IConfiguration configuration)
    {
        _connectionString = configuration.GetConnectionString("TraceCarrierDb")
            ?? throw new InvalidOperationException("Connection string 'TraceCarrierDb' was not found.");

        EnsureSchema();
    }

    public string GenerateUnitId()
    {
        using var connection = OpenConnection();
        return GenerateUniqueBusinessId(
            connection,
            "UNIT",
            UnitBusinessIdExists);
    }

    public string GenerateCarrierId()
    {
        using var connection = OpenConnection();
        return GenerateUniqueBusinessId(
            connection,
            "CAR",
            CarrierBusinessIdExists);
    }

    public Unit CreateUnit(CreateUnitRequest request)
    {
        var currentProcess = RequireValue(request.CurrentProcess, nameof(request.CurrentProcess));
        var status = RequireValue(request.Status, nameof(request.Status));

        using var connection = OpenConnection();
        var businessId = ResolveUnitBusinessId(connection, request.UnitId, nameof(request.UnitId));

        using var command = new NpgsqlCommand(
            """
            INSERT INTO units (unit_id, current_process, status)
            VALUES (@unit_id, @current_process, @status)
            RETURNING id, unit_id, current_process, status, next_process_available_at, created_at, updated_at;
            """,
            connection);

        command.Parameters.AddWithValue("unit_id", businessId);
        command.Parameters.AddWithValue("current_process", currentProcess);
        command.Parameters.AddWithValue("status", status);

        try
        {
            using var reader = command.ExecuteReader();
            reader.Read();
            return MapUnit(reader);
        }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            throw new InvalidOperationException($"Unit '{businessId}' already exists.");
        }
    }

    public Carrier CreateCarrier(CreateCarrierRequest request)
    {
        var status = RequireValue(request.Status, nameof(request.Status));

        using var connection = OpenConnection();
        var businessId = ResolveCarrierBusinessId(connection, request.CarrierId, nameof(request.CarrierId));

        using var command = new NpgsqlCommand(
            """
            INSERT INTO carriers (carrier_id, status)
            VALUES (@carrier_id, @status)
            RETURNING id, carrier_id, status, next_process_available_at, created_at, updated_at;
            """,
            connection);

        command.Parameters.AddWithValue("carrier_id", businessId);
        command.Parameters.AddWithValue("status", status);

        try
        {
            using var reader = command.ExecuteReader();
            reader.Read();
            return MapCarrier(reader);
        }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            throw new InvalidOperationException($"Carrier '{businessId}' already exists.");
        }
    }

    public CreateCarrierFromUnitsResult CreateCarrierFromUnits(CreateCarrierFromUnitsRequest request)
    {
        var requestedUnitIds = request.UnitIds
            .Select(unitId => RequireValue(unitId, nameof(request.UnitIds)))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (requestedUnitIds.Length == 0)
        {
            throw new ArgumentException("At least one unit ID is required.");
        }

        var carrierStatus = RequireValue(request.CarrierStatus, nameof(request.CarrierStatus));

        using var connection = OpenConnection();
        using var transaction = connection.BeginTransaction();

        var carrierBusinessId = ResolveCarrierBusinessId(connection, request.CarrierId, nameof(request.CarrierId));
        Carrier createdCarrier;

        using (var createCarrier = new NpgsqlCommand(
                   """
                   INSERT INTO carriers (carrier_id, status)
                   VALUES (@carrier_id, @status)
                   RETURNING id, carrier_id, status, next_process_available_at, created_at, updated_at;
                   """,
                   connection,
                   transaction))
        {
            createCarrier.Parameters.AddWithValue("carrier_id", carrierBusinessId);
            createCarrier.Parameters.AddWithValue("status", carrierStatus);

            using var reader = createCarrier.ExecuteReader();
            reader.Read();
            createdCarrier = MapCarrier(reader);
        }

        var assignedUnitIds = new List<string>();
        foreach (var unitBusinessId in requestedUnitIds)
        {
            var unit = GetRequiredUnitByBusinessId(connection, transaction, unitBusinessId, forUpdate: true);
            ValidateEntityReadyForNextStep(unit.NextProcessAvailableAt, "Unit", unitBusinessId);

            if (UnitHasCarrierAssignment(connection, transaction, unit.Id))
            {
                throw new InvalidOperationException($"Unit '{unitBusinessId}' is already assigned to a carrier.");
            }

            InsertCarrierUnitAssignment(connection, transaction, createdCarrier.Id, unit.Id);

            using var updateUnit = new NpgsqlCommand(
                """
                UPDATE units
                SET status = 'assigned_to_carrier',
                    current_process = 'carrier_assignment',
                    next_process_available_at = NULL
                WHERE id = @unit_db_id;
                """,
                connection,
                transaction);

            updateUnit.Parameters.AddWithValue("unit_db_id", unit.Id);
            updateUnit.ExecuteNonQuery();
            assignedUnitIds.Add(unit.UnitId);
        }

        using (var updateCarrier = new NpgsqlCommand(
                   """
                   UPDATE carriers
                   SET status = 'loaded'
                   WHERE id = @carrier_db_id
                   RETURNING id, carrier_id, status, next_process_available_at, created_at, updated_at;
                   """,
                   connection,
                   transaction))
        {
            updateCarrier.Parameters.AddWithValue("carrier_db_id", createdCarrier.Id);
            using var reader = updateCarrier.ExecuteReader();
            reader.Read();
            createdCarrier = MapCarrier(reader);
        }

        transaction.Commit();
        return new CreateCarrierFromUnitsResult
        {
            Carrier = createdCarrier,
            AssignedUnitIds = assignedUnitIds
        };
    }

    public CarrierUnit AssignUnitToCarrier(string carrierId, string unitId)
    {
        var carrierBusinessId = RequireValue(carrierId, nameof(carrierId));
        var unitBusinessId = RequireValue(unitId, nameof(unitId));

        using var connection = OpenConnection();
        using var transaction = connection.BeginTransaction();

        var unit = GetRequiredUnitByBusinessId(connection, transaction, unitBusinessId, forUpdate: true);
        var carrier = GetRequiredCarrierByBusinessId(connection, transaction, carrierBusinessId, forUpdate: true);
        ValidateEntityReadyForNextStep(unit.NextProcessAvailableAt, "Unit", unitBusinessId);

        if (UnitHasCarrierAssignment(connection, transaction, unit.Id))
        {
            throw new InvalidOperationException($"Unit '{unitBusinessId}' is already assigned to a carrier.");
        }

        var assignment = InsertCarrierUnitAssignment(connection, transaction, carrier.Id, unit.Id);

        using (var updateUnit = new NpgsqlCommand(
                   """
                   UPDATE units
                   SET status = 'assigned_to_carrier',
                       current_process = 'carrier_assignment',
                       next_process_available_at = NULL
                   WHERE id = @unit_db_id;
                   """,
                   connection,
                   transaction))
        {
            updateUnit.Parameters.AddWithValue("unit_db_id", unit.Id);
            updateUnit.ExecuteNonQuery();
        }

        using (var updateCarrier = new NpgsqlCommand(
                   """
                   UPDATE carriers
                   SET status = 'loaded'
                   WHERE id = @carrier_db_id;
                   """,
                   connection,
                   transaction))
        {
            updateCarrier.Parameters.AddWithValue("carrier_db_id", carrier.Id);
            updateCarrier.ExecuteNonQuery();
        }

        transaction.Commit();
        return assignment;
    }

    public ProcessHistory StartUnitProcess(string unitId, StartProcessRequest request)
    {
        var unitBusinessId = RequireValue(unitId, nameof(unitId));
        var processName = RequireValue(request.ProcessName, nameof(request.ProcessName));
        if (request.RequiredTimeSeconds < 0)
        {
            throw new ArgumentException("RequiredTimeSeconds cannot be negative.");
        }

        using var connection = OpenConnection();
        using var transaction = connection.BeginTransaction();

        var unit = GetRequiredUnitByBusinessId(connection, transaction, unitBusinessId, forUpdate: true);
        var startTime = DateTimeOffset.UtcNow;
        var readyForNextProcessAt = startTime.AddSeconds(request.RequiredTimeSeconds);

        ProcessHistory process;
        using (var insertProcess = new NpgsqlCommand(
                   """
                   INSERT INTO process_history (
                       unit_id,
                       process_name,
                       start_time,
                       required_time_seconds,
                       ready_for_next_process_at,
                       completed,
                       notes)
                   VALUES (
                       @unit_db_id,
                       @process_name,
                       @start_time,
                       @required_time_seconds,
                       @ready_for_next_process_at,
                       FALSE,
                       @notes)
                   RETURNING id, unit_id, process_name, start_time, end_time, required_time_seconds, ready_for_next_process_at, completed, notes;
                   """,
                   connection,
                   transaction))
        {
            insertProcess.Parameters.AddWithValue("unit_db_id", unit.Id);
            insertProcess.Parameters.AddWithValue("process_name", processName);
            insertProcess.Parameters.AddWithValue("start_time", startTime);
            insertProcess.Parameters.AddWithValue("required_time_seconds", request.RequiredTimeSeconds);
            insertProcess.Parameters.AddWithValue("ready_for_next_process_at", readyForNextProcessAt);
            insertProcess.Parameters.AddWithValue("notes", (object?)request.Notes?.Trim() ?? DBNull.Value);

            using var reader = insertProcess.ExecuteReader();
            reader.Read();
            process = MapProcessHistory(reader);
        }

        using (var updateUnit = new NpgsqlCommand(
                   """
                   UPDATE units
                   SET current_process = @process_name,
                       status = 'in_process',
                       next_process_available_at = @ready_for_next_process_at
                   WHERE id = @unit_db_id;
                   """,
                   connection,
                   transaction))
        {
            updateUnit.Parameters.AddWithValue("process_name", processName);
            updateUnit.Parameters.AddWithValue("ready_for_next_process_at", readyForNextProcessAt);
            updateUnit.Parameters.AddWithValue("unit_db_id", unit.Id);
            updateUnit.ExecuteNonQuery();
        }

        transaction.Commit();
        return process;
    }

    public ProcessHistory CompleteUnitProcess(string unitId, string processName)
    {
        var unitBusinessId = RequireValue(unitId, nameof(unitId));
        var processNameValue = RequireValue(processName, nameof(processName));

        using var connection = OpenConnection();
        using var transaction = connection.BeginTransaction();

        var unit = GetRequiredUnitByBusinessId(connection, transaction, unitBusinessId, forUpdate: true);

        ProcessHistory openProcess;
        using (var selectProcess = new NpgsqlCommand(
                   """
                   SELECT id, unit_id, process_name, start_time, end_time, required_time_seconds, ready_for_next_process_at, completed, notes
                   FROM process_history
                   WHERE unit_id = @unit_db_id
                     AND process_name = @process_name
                     AND completed = FALSE
                   ORDER BY start_time DESC
                   LIMIT 1
                   FOR UPDATE;
                   """,
                   connection,
                   transaction))
        {
            selectProcess.Parameters.AddWithValue("unit_db_id", unit.Id);
            selectProcess.Parameters.AddWithValue("process_name", processNameValue);

            using var reader = selectProcess.ExecuteReader();
            if (!reader.Read())
            {
                throw new InvalidOperationException(
                    $"No open process '{processNameValue}' was found for unit '{unitBusinessId}'.");
            }

            openProcess = MapProcessHistory(reader);
        }

        ValidateEntityReadyForNextStep(openProcess.ReadyForNextProcessAt, "Process", processNameValue);

        ProcessHistory completedProcess;
        using (var completeProcess = new NpgsqlCommand(
                   """
                   UPDATE process_history
                   SET end_time = @end_time,
                       completed = TRUE
                   WHERE id = @process_id
                   RETURNING id, unit_id, process_name, start_time, end_time, required_time_seconds, ready_for_next_process_at, completed, notes;
                   """,
                   connection,
                   transaction))
        {
            completeProcess.Parameters.AddWithValue("end_time", DateTimeOffset.UtcNow);
            completeProcess.Parameters.AddWithValue("process_id", openProcess.Id);

            using var reader = completeProcess.ExecuteReader();
            reader.Read();
            completedProcess = MapProcessHistory(reader);
        }

        using (var updateUnit = new NpgsqlCommand(
                   """
                   UPDATE units
                   SET status = 'process_completed',
                       next_process_available_at = @ready_for_next_process_at
                   WHERE id = @unit_db_id;
                   """,
                   connection,
                   transaction))
        {
            updateUnit.Parameters.AddWithValue("ready_for_next_process_at", openProcess.ReadyForNextProcessAt);
            updateUnit.Parameters.AddWithValue("unit_db_id", unit.Id);
            updateUnit.ExecuteNonQuery();
        }

        transaction.Commit();
        return completedProcess;
    }

    public Unit? GetUnitByBusinessId(string unitId)
    {
        var unitBusinessId = RequireValue(unitId, nameof(unitId));

        using var connection = OpenConnection();
        using var command = new NpgsqlCommand(
            """
            SELECT id, unit_id, current_process, status, next_process_available_at, created_at, updated_at
            FROM units
            WHERE unit_id = @unit_id;
            """,
            connection);

        command.Parameters.AddWithValue("unit_id", unitBusinessId);
        using var reader = command.ExecuteReader();
        return reader.Read() ? MapUnit(reader) : null;
    }

    public Carrier? GetCarrierByBusinessId(string carrierId)
    {
        var carrierBusinessId = RequireValue(carrierId, nameof(carrierId));

        using var connection = OpenConnection();
        using var command = new NpgsqlCommand(
            """
            SELECT id, carrier_id, status, next_process_available_at, created_at, updated_at
            FROM carriers
            WHERE carrier_id = @carrier_id;
            """,
            connection);

        command.Parameters.AddWithValue("carrier_id", carrierBusinessId);
        using var reader = command.ExecuteReader();
        return reader.Read() ? MapCarrier(reader) : null;
    }

    public IReadOnlyCollection<ProcessHistory> GetUnitProcessHistory(string unitId)
    {
        var unitBusinessId = RequireValue(unitId, nameof(unitId));

        using var connection = OpenConnection();
        var unit = GetRequiredUnitByBusinessId(connection, transaction: null, unitBusinessId, forUpdate: false);
        var results = new List<ProcessHistory>();

        using var command = new NpgsqlCommand(
            """
            SELECT id, unit_id, process_name, start_time, end_time, required_time_seconds, ready_for_next_process_at, completed, notes
            FROM process_history
            WHERE unit_id = @unit_db_id
            ORDER BY start_time;
            """,
            connection);

        command.Parameters.AddWithValue("unit_db_id", unit.Id);
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            results.Add(MapProcessHistory(reader));
        }

        return results;
    }

    public CarrierProcessHistory StartCarrierProcess(string carrierId, StartProcessRequest request)
    {
        var carrierBusinessId = RequireValue(carrierId, nameof(carrierId));
        var processName = RequireValue(request.ProcessName, nameof(request.ProcessName));
        if (request.RequiredTimeSeconds < 0)
        {
            throw new ArgumentException("RequiredTimeSeconds cannot be negative.");
        }

        using var connection = OpenConnection();
        using var transaction = connection.BeginTransaction();

        var carrier = GetRequiredCarrierByBusinessId(connection, transaction, carrierBusinessId, forUpdate: true);
        var startTime = DateTimeOffset.UtcNow;
        var readyForNextProcessAt = startTime.AddSeconds(request.RequiredTimeSeconds);

        CarrierProcessHistory process;
        using (var insertProcess = new NpgsqlCommand(
                   """
                   INSERT INTO carrier_process_history (
                       carrier_id,
                       process_name,
                       start_time,
                       required_time_seconds,
                       ready_for_next_process_at,
                       completed,
                       notes)
                   VALUES (
                       @carrier_db_id,
                       @process_name,
                       @start_time,
                       @required_time_seconds,
                       @ready_for_next_process_at,
                       FALSE,
                       @notes)
                   RETURNING id, carrier_id, process_name, start_time, end_time, required_time_seconds, ready_for_next_process_at, completed, notes;
                   """,
                   connection,
                   transaction))
        {
            insertProcess.Parameters.AddWithValue("carrier_db_id", carrier.Id);
            insertProcess.Parameters.AddWithValue("process_name", processName);
            insertProcess.Parameters.AddWithValue("start_time", startTime);
            insertProcess.Parameters.AddWithValue("required_time_seconds", request.RequiredTimeSeconds);
            insertProcess.Parameters.AddWithValue("ready_for_next_process_at", readyForNextProcessAt);
            insertProcess.Parameters.AddWithValue("notes", (object?)request.Notes?.Trim() ?? DBNull.Value);

            using var reader = insertProcess.ExecuteReader();
            reader.Read();
            process = MapCarrierProcessHistory(reader);
        }

        using (var updateCarrier = new NpgsqlCommand(
                   """
                   UPDATE carriers
                   SET status = 'in_process',
                       next_process_available_at = @ready_for_next_process_at
                   WHERE id = @carrier_db_id;
                   """,
                   connection,
                   transaction))
        {
            updateCarrier.Parameters.AddWithValue("ready_for_next_process_at", readyForNextProcessAt);
            updateCarrier.Parameters.AddWithValue("carrier_db_id", carrier.Id);
            updateCarrier.ExecuteNonQuery();
        }

        transaction.Commit();
        return process;
    }

    public CarrierProcessHistory CompleteCarrierProcess(string carrierId, string processName)
    {
        var carrierBusinessId = RequireValue(carrierId, nameof(carrierId));
        var processNameValue = RequireValue(processName, nameof(processName));

        using var connection = OpenConnection();
        using var transaction = connection.BeginTransaction();

        var carrier = GetRequiredCarrierByBusinessId(connection, transaction, carrierBusinessId, forUpdate: true);

        CarrierProcessHistory openProcess;
        using (var selectProcess = new NpgsqlCommand(
                   """
                   SELECT id, carrier_id, process_name, start_time, end_time, required_time_seconds, ready_for_next_process_at, completed, notes
                   FROM carrier_process_history
                   WHERE carrier_id = @carrier_db_id
                     AND process_name = @process_name
                     AND completed = FALSE
                   ORDER BY start_time DESC
                   LIMIT 1
                   FOR UPDATE;
                   """,
                   connection,
                   transaction))
        {
            selectProcess.Parameters.AddWithValue("carrier_db_id", carrier.Id);
            selectProcess.Parameters.AddWithValue("process_name", processNameValue);

            using var reader = selectProcess.ExecuteReader();
            if (!reader.Read())
            {
                throw new InvalidOperationException(
                    $"No open process '{processNameValue}' was found for carrier '{carrierBusinessId}'.");
            }

            openProcess = MapCarrierProcessHistory(reader);
        }

        ValidateEntityReadyForNextStep(openProcess.ReadyForNextProcessAt, "Carrier process", processNameValue);

        CarrierProcessHistory completedProcess;
        using (var completeProcess = new NpgsqlCommand(
                   """
                   UPDATE carrier_process_history
                   SET end_time = @end_time,
                       completed = TRUE
                   WHERE id = @process_id
                   RETURNING id, carrier_id, process_name, start_time, end_time, required_time_seconds, ready_for_next_process_at, completed, notes;
                   """,
                   connection,
                   transaction))
        {
            completeProcess.Parameters.AddWithValue("end_time", DateTimeOffset.UtcNow);
            completeProcess.Parameters.AddWithValue("process_id", openProcess.Id);

            using var reader = completeProcess.ExecuteReader();
            reader.Read();
            completedProcess = MapCarrierProcessHistory(reader);
        }

        using (var updateCarrier = new NpgsqlCommand(
                   """
                   UPDATE carriers
                   SET status = 'process_completed',
                       next_process_available_at = @ready_for_next_process_at
                   WHERE id = @carrier_db_id;
                   """,
                   connection,
                   transaction))
        {
            updateCarrier.Parameters.AddWithValue("ready_for_next_process_at", openProcess.ReadyForNextProcessAt);
            updateCarrier.Parameters.AddWithValue("carrier_db_id", carrier.Id);
            updateCarrier.ExecuteNonQuery();
        }

        transaction.Commit();
        return completedProcess;
    }

    public IReadOnlyCollection<CarrierProcessHistory> GetCarrierProcessHistory(string carrierId)
    {
        var carrierBusinessId = RequireValue(carrierId, nameof(carrierId));

        using var connection = OpenConnection();
        var carrier = GetRequiredCarrierByBusinessId(connection, transaction: null, carrierBusinessId, forUpdate: false);
        var results = new List<CarrierProcessHistory>();

        using var command = new NpgsqlCommand(
            """
            SELECT id, carrier_id, process_name, start_time, end_time, required_time_seconds, ready_for_next_process_at, completed, notes
            FROM carrier_process_history
            WHERE carrier_id = @carrier_db_id
            ORDER BY start_time;
            """,
            connection);

        command.Parameters.AddWithValue("carrier_db_id", carrier.Id);
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            results.Add(MapCarrierProcessHistory(reader));
        }

        return results;
    }

    public FinalizeCarrierResult FinalizeCarrier(
        string carrierId,
        IReadOnlyCollection<string> unitIdsToRelease,
        string releasedUnitStatus,
        string releasedUnitProcess)
    {
        var carrierBusinessId = RequireValue(carrierId, nameof(carrierId));
        var releasedStatus = RequireValue(releasedUnitStatus, nameof(releasedUnitStatus));
        var releasedProcess = RequireValue(releasedUnitProcess, nameof(releasedUnitProcess));

        using var connection = OpenConnection();
        using var transaction = connection.BeginTransaction();

        var carrier = GetRequiredCarrierByBusinessId(connection, transaction, carrierBusinessId, forUpdate: true);
        ValidateEntityReadyForNextStep(carrier.NextProcessAvailableAt, "Carrier", carrierBusinessId);
        var assignedUnits = GetAssignedUnits(connection, transaction, carrier.Id);

        var requestedReleaseSet = unitIdsToRelease
            .Select(value => value.Trim())
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var releaseAllAssigned = requestedReleaseSet.Count == 0;
        var releasedUnitIds = new List<string>();
        var remainingUnitIds = new List<string>();

        foreach (var unit in assignedUnits)
        {
            var shouldRelease = releaseAllAssigned || requestedReleaseSet.Contains(unit.UnitId);
            if (!shouldRelease)
            {
                remainingUnitIds.Add(unit.UnitId);
                continue;
            }

            using (var deleteRelation = new NpgsqlCommand(
                       """
                       DELETE FROM carrier_units
                       WHERE carrier_id = @carrier_db_id
                         AND unit_id = @unit_db_id;
                       """,
                       connection,
                       transaction))
            {
                deleteRelation.Parameters.AddWithValue("carrier_db_id", carrier.Id);
                deleteRelation.Parameters.AddWithValue("unit_db_id", unit.Id);
                deleteRelation.ExecuteNonQuery();
            }

            using (var updateUnit = new NpgsqlCommand(
                       """
                    UPDATE units
                    SET status = @status,
                        current_process = @current_process,
                        next_process_available_at = NULL
                    WHERE id = @unit_db_id;
                    """,
                    connection,
                    transaction))
            {
                updateUnit.Parameters.AddWithValue("status", releasedStatus);
                updateUnit.Parameters.AddWithValue("current_process", releasedProcess);
                updateUnit.Parameters.AddWithValue("unit_db_id", unit.Id);
                updateUnit.ExecuteNonQuery();
            }

            releasedUnitIds.Add(unit.UnitId);
        }

        using (var updateCarrier = new NpgsqlCommand(
                   """
                   UPDATE carriers
                   SET status = @status
                   WHERE id = @carrier_db_id;
                   """,
                   connection,
                   transaction))
        {
            var nextStatus = remainingUnitIds.Count == 0 ? "unloaded" : "partially_unloaded";
            updateCarrier.Parameters.AddWithValue("status", nextStatus);
            updateCarrier.Parameters.AddWithValue("carrier_db_id", carrier.Id);
            updateCarrier.ExecuteNonQuery();
        }

        transaction.Commit();
        return new FinalizeCarrierResult
        {
            CarrierId = carrierBusinessId,
            ReleasedUnitIds = releasedUnitIds,
            RemainingAssignedUnitIds = remainingUnitIds
        };
    }

    private void EnsureSchema()
    {
        if (_schemaInitialized)
        {
            return;
        }

        lock (SchemaSync)
        {
            if (_schemaInitialized)
            {
                return;
            }

            using var connection = OpenConnection();
            using var command = new NpgsqlCommand(
                """
                ALTER TABLE IF EXISTS units
                    ADD COLUMN IF NOT EXISTS next_process_available_at TIMESTAMPTZ NULL;

                ALTER TABLE IF EXISTS carriers
                    ADD COLUMN IF NOT EXISTS next_process_available_at TIMESTAMPTZ NULL;

                ALTER TABLE IF EXISTS process_history
                    ADD COLUMN IF NOT EXISTS ready_for_next_process_at TIMESTAMPTZ NOT NULL DEFAULT NOW();

                CREATE TABLE IF NOT EXISTS carrier_process_history (
                    id BIGSERIAL PRIMARY KEY,
                    carrier_id BIGINT NOT NULL REFERENCES carriers(id) ON DELETE CASCADE,
                    process_name VARCHAR(100) NOT NULL,
                    start_time TIMESTAMPTZ NOT NULL DEFAULT NOW(),
                    end_time TIMESTAMPTZ NULL,
                    required_time_seconds INTEGER NOT NULL CHECK (required_time_seconds >= 0),
                    ready_for_next_process_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
                    completed BOOLEAN NOT NULL DEFAULT FALSE,
                    notes TEXT NULL,
                    CONSTRAINT chk_carrier_process_time_window CHECK (
                        end_time IS NULL OR end_time >= start_time
                    ),
                    CONSTRAINT chk_carrier_process_ready_for_next CHECK (
                        ready_for_next_process_at >= start_time
                    )
                );

                ALTER TABLE IF EXISTS carrier_process_history
                    ADD COLUMN IF NOT EXISTS ready_for_next_process_at TIMESTAMPTZ NOT NULL DEFAULT NOW();

                CREATE INDEX IF NOT EXISTS idx_carrier_process_history_carrier_id
                    ON carrier_process_history(carrier_id);
                CREATE INDEX IF NOT EXISTS idx_carrier_process_history_carrier_completed
                    ON carrier_process_history(carrier_id, completed);
                CREATE INDEX IF NOT EXISTS idx_carrier_process_history_process_name
                    ON carrier_process_history(process_name);
                CREATE INDEX IF NOT EXISTS idx_units_next_process_available_at
                    ON units(next_process_available_at);
                CREATE INDEX IF NOT EXISTS idx_carriers_next_process_available_at
                    ON carriers(next_process_available_at);
                CREATE INDEX IF NOT EXISTS idx_process_history_ready_for_next
                    ON process_history(ready_for_next_process_at);
                CREATE INDEX IF NOT EXISTS idx_carrier_process_history_ready_for_next
                    ON carrier_process_history(ready_for_next_process_at);
                """,
                connection);

            command.ExecuteNonQuery();
            _schemaInitialized = true;
        }
    }

    private static string ResolveUnitBusinessId(
        NpgsqlConnection connection,
        string? requestedBusinessId,
        string argumentName)
    {
        if (!string.IsNullOrWhiteSpace(requestedBusinessId))
        {
            var businessId = RequireValue(requestedBusinessId, argumentName);
            if (UnitBusinessIdExists(connection, businessId))
            {
                throw new InvalidOperationException($"Unit '{businessId}' already exists.");
            }

            return businessId;
        }

        return GenerateUniqueBusinessId(connection, "UNIT", UnitBusinessIdExists);
    }

    private static string ResolveCarrierBusinessId(
        NpgsqlConnection connection,
        string? requestedBusinessId,
        string argumentName)
    {
        if (!string.IsNullOrWhiteSpace(requestedBusinessId))
        {
            var businessId = RequireValue(requestedBusinessId, argumentName);
            if (CarrierBusinessIdExists(connection, businessId))
            {
                throw new InvalidOperationException($"Carrier '{businessId}' already exists.");
            }

            return businessId;
        }

        return GenerateUniqueBusinessId(connection, "CAR", CarrierBusinessIdExists);
    }

    private static string GenerateUniqueBusinessId(
        NpgsqlConnection connection,
        string prefix,
        Func<NpgsqlConnection, string, bool> existsFunc)
    {
        for (var attempt = 0; attempt < 20; attempt++)
        {
            var candidate = $"{prefix}-{Guid.NewGuid():N}".ToUpperInvariant();
            if (!existsFunc(connection, candidate))
            {
                return candidate;
            }
        }

        throw new InvalidOperationException("Failed to generate a unique business id.");
    }

    private static bool UnitBusinessIdExists(NpgsqlConnection connection, string unitBusinessId)
    {
        using var command = new NpgsqlCommand(
            """
            SELECT 1
            FROM units
            WHERE unit_id = @unit_id
            LIMIT 1;
            """,
            connection);

        command.Parameters.AddWithValue("unit_id", unitBusinessId);
        return command.ExecuteScalar() is not null;
    }

    private static bool CarrierBusinessIdExists(NpgsqlConnection connection, string carrierBusinessId)
    {
        using var command = new NpgsqlCommand(
            """
            SELECT 1
            FROM carriers
            WHERE carrier_id = @carrier_id
            LIMIT 1;
            """,
            connection);

        command.Parameters.AddWithValue("carrier_id", carrierBusinessId);
        return command.ExecuteScalar() is not null;
    }

    private static bool UnitHasCarrierAssignment(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        long unitDbId)
    {
        using var command = new NpgsqlCommand(
            """
            SELECT 1
            FROM carrier_units
            WHERE unit_id = @unit_db_id
            LIMIT 1;
            """,
            connection,
            transaction);

        command.Parameters.AddWithValue("unit_db_id", unitDbId);
        return command.ExecuteScalar() is not null;
    }

    private static CarrierUnit InsertCarrierUnitAssignment(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        long carrierDbId,
        long unitDbId)
    {
        using var command = new NpgsqlCommand(
            """
            INSERT INTO carrier_units (carrier_id, unit_id)
            VALUES (@carrier_db_id, @unit_db_id)
            RETURNING id, carrier_id, unit_id, assigned_at;
            """,
            connection,
            transaction);

        command.Parameters.AddWithValue("carrier_db_id", carrierDbId);
        command.Parameters.AddWithValue("unit_db_id", unitDbId);

        try
        {
            using var reader = command.ExecuteReader();
            reader.Read();
            return MapCarrierUnit(reader);
        }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            throw new InvalidOperationException("Unit is already assigned to a carrier.");
        }
    }

    private static List<(long Id, string UnitId)> GetAssignedUnits(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        long carrierDbId)
    {
        var results = new List<(long Id, string UnitId)>();
        using var command = new NpgsqlCommand(
            """
            SELECT u.id, u.unit_id
            FROM carrier_units cu
            INNER JOIN units u ON u.id = cu.unit_id
            WHERE cu.carrier_id = @carrier_db_id
            ORDER BY u.id;
            """,
            connection,
            transaction);

        command.Parameters.AddWithValue("carrier_db_id", carrierDbId);
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            results.Add((reader.GetInt64(reader.GetOrdinal("id")), reader.GetString(reader.GetOrdinal("unit_id"))));
        }

        return results;
    }

    private static Unit GetRequiredUnitByBusinessId(
        NpgsqlConnection connection,
        NpgsqlTransaction? transaction,
        string unitBusinessId,
        bool forUpdate)
    {
        var sql =
            """
            SELECT id, unit_id, current_process, status, next_process_available_at, created_at, updated_at
            FROM units
            WHERE unit_id = @unit_id
            """ + (forUpdate ? " FOR UPDATE;" : ";");

        using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("unit_id", unitBusinessId);
        using var reader = command.ExecuteReader();

        if (!reader.Read())
        {
            throw new KeyNotFoundException($"Unit '{unitBusinessId}' was not found.");
        }

        return MapUnit(reader);
    }

    private static Carrier GetRequiredCarrierByBusinessId(
        NpgsqlConnection connection,
        NpgsqlTransaction? transaction,
        string carrierBusinessId,
        bool forUpdate)
    {
        var sql =
            """
            SELECT id, carrier_id, status, next_process_available_at, created_at, updated_at
            FROM carriers
            WHERE carrier_id = @carrier_id
            """ + (forUpdate ? " FOR UPDATE;" : ";");

        using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("carrier_id", carrierBusinessId);
        using var reader = command.ExecuteReader();

        if (!reader.Read())
        {
            throw new KeyNotFoundException($"Carrier '{carrierBusinessId}' was not found.");
        }

        return MapCarrier(reader);
    }

    private static void ValidateEntityReadyForNextStep(
        DateTimeOffset? readyForNextProcessAt,
        string entityType,
        string identifier)
    {
        if (readyForNextProcessAt is null)
        {
            return;
        }

        var remainingSeconds = Math.Ceiling((readyForNextProcessAt.Value - DateTimeOffset.UtcNow).TotalSeconds);
        if (remainingSeconds > 0)
        {
            throw new InvalidOperationException(
                $"{entityType} '{identifier}' cannot move to the next step yet. " +
                $"Allowed at {readyForNextProcessAt:O}. Remaining seconds: {remainingSeconds}.");
        }
    }

    private NpgsqlConnection OpenConnection()
    {
        var connection = new NpgsqlConnection(_connectionString);
        connection.Open();
        return connection;
    }

    private static string RequireValue(string? value, string argumentName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException($"{argumentName} is required.");
        }

        return value.Trim();
    }

    private static Unit MapUnit(IDataRecord row) =>
        new()
        {
            Id = row.GetInt64(row.GetOrdinal("id")),
            UnitId = row.GetString(row.GetOrdinal("unit_id")),
            CurrentProcess = row.GetString(row.GetOrdinal("current_process")),
            Status = row.GetString(row.GetOrdinal("status")),
            NextProcessAvailableAt = ReadNullableDateTimeOffset(row, "next_process_available_at"),
            CreatedAt = ReadDateTimeOffset(row, "created_at"),
            UpdatedAt = ReadDateTimeOffset(row, "updated_at")
        };

    private static Carrier MapCarrier(IDataRecord row) =>
        new()
        {
            Id = row.GetInt64(row.GetOrdinal("id")),
            CarrierId = row.GetString(row.GetOrdinal("carrier_id")),
            Status = row.GetString(row.GetOrdinal("status")),
            NextProcessAvailableAt = ReadNullableDateTimeOffset(row, "next_process_available_at"),
            CreatedAt = ReadDateTimeOffset(row, "created_at"),
            UpdatedAt = ReadDateTimeOffset(row, "updated_at")
        };

    private static CarrierUnit MapCarrierUnit(IDataRecord row) =>
        new()
        {
            Id = row.GetInt64(row.GetOrdinal("id")),
            CarrierId = row.GetInt64(row.GetOrdinal("carrier_id")),
            UnitId = row.GetInt64(row.GetOrdinal("unit_id")),
            AssignedAt = ReadDateTimeOffset(row, "assigned_at")
        };

    private static ProcessHistory MapProcessHistory(IDataRecord row) =>
        new()
        {
            Id = row.GetInt64(row.GetOrdinal("id")),
            UnitId = row.GetInt64(row.GetOrdinal("unit_id")),
            ProcessName = row.GetString(row.GetOrdinal("process_name")),
            StartTime = ReadDateTimeOffset(row, "start_time"),
            EndTime = ReadNullableDateTimeOffset(row, "end_time"),
            RequiredTimeSeconds = row.GetInt32(row.GetOrdinal("required_time_seconds")),
            ReadyForNextProcessAt = ReadDateTimeOffset(row, "ready_for_next_process_at"),
            Completed = row.GetBoolean(row.GetOrdinal("completed")),
            Notes = row.IsDBNull(row.GetOrdinal("notes")) ? null : row.GetString(row.GetOrdinal("notes"))
        };

    private static CarrierProcessHistory MapCarrierProcessHistory(IDataRecord row) =>
        new()
        {
            Id = row.GetInt64(row.GetOrdinal("id")),
            CarrierId = row.GetInt64(row.GetOrdinal("carrier_id")),
            ProcessName = row.GetString(row.GetOrdinal("process_name")),
            StartTime = ReadDateTimeOffset(row, "start_time"),
            EndTime = ReadNullableDateTimeOffset(row, "end_time"),
            RequiredTimeSeconds = row.GetInt32(row.GetOrdinal("required_time_seconds")),
            ReadyForNextProcessAt = ReadDateTimeOffset(row, "ready_for_next_process_at"),
            Completed = row.GetBoolean(row.GetOrdinal("completed")),
            Notes = row.IsDBNull(row.GetOrdinal("notes")) ? null : row.GetString(row.GetOrdinal("notes"))
        };

    private static DateTimeOffset ReadDateTimeOffset(IDataRecord row, string columnName)
    {
        var ordinal = row.GetOrdinal(columnName);
        var value = row.GetValue(ordinal);
        return value switch
        {
            DateTimeOffset dto => dto,
            DateTime dt => new DateTimeOffset(DateTime.SpecifyKind(dt, DateTimeKind.Utc)),
            _ => throw new InvalidOperationException(
                $"Column '{columnName}' cannot be converted to DateTimeOffset.")
        };
    }

    private static DateTimeOffset? ReadNullableDateTimeOffset(IDataRecord row, string columnName)
    {
        var ordinal = row.GetOrdinal(columnName);
        if (row.IsDBNull(ordinal))
        {
            return null;
        }

        var value = row.GetValue(ordinal);
        return value switch
        {
            DateTimeOffset dto => dto,
            DateTime dt => new DateTimeOffset(DateTime.SpecifyKind(dt, DateTimeKind.Utc)),
            _ => throw new InvalidOperationException(
                $"Column '{columnName}' cannot be converted to nullable DateTimeOffset.")
        };
    }
}
