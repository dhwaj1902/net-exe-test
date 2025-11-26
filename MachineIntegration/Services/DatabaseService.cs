using MySqlConnector;
using MachineIntegration.Models;
using MachineIntegration.Utilities;

namespace MachineIntegration.Services;

public class DatabaseService
{
    private readonly string _connectionString;
    private readonly string _machineName;
    private readonly string _host;
    private readonly string _database;
    private readonly Logger _logger;
    
    public string Host => _host;
    public string Database => _database;
    public string LastError { get; private set; } = "";

    public DatabaseService(MachineConfig config)
    {
        _connectionString = config.GetConnectionString();
        _machineName = config.MachineName;
        _host = config.Host;
        _database = config.Database;
        _logger = new Logger("Database");
    }

    /// <summary>
    /// Test database connection
    /// </summary>
    public async Task<bool> TestConnectionAsync()
    {
        try
        {
            _logger.Log($"Attempting connection to {_host}:{_database}");
            
            await using var connection = new MySqlConnection(_connectionString);
            
            // Set a cancellation token for timeout
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await connection.OpenAsync(cts.Token);
            
            _logger.Log($"Database connection successful: {connection.Database}@{connection.DataSource}");
            LastError = "";
            return true;
        }
        catch (OperationCanceledException)
        {
            LastError = $"Connection timeout to {_host}";
            _logger.LogError(LastError);
            return false;
        }
        catch (MySqlException ex)
        {
            LastError = $"MySQL Error ({ex.Number}): {ex.Message}";
            _logger.LogError("Database connection failed", ex);
            return false;
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            _logger.LogError("Database connection failed", ex);
            return false;
        }
    }

    /// <summary>
    /// Insert machine reading data
    /// INSERT INTO flabs_machinedata (LabNo, Machine_ID, Machine_Param, Reading, isImage, imageType, ImageUrl)
    /// </summary>
    public async Task<bool> InsertMachineDataAsync(MachineReading reading)
    {
        try
        {
            await using var connection = new MySqlConnection(_connectionString);
            await connection.OpenAsync();

            const string sql = @"
                INSERT INTO flabs_machinedata 
                (LabNo, Machine_ID, Machine_Param, Reading, isImage, imageType, ImageUrl) 
                VALUES (@LabNo, @MachineId, @MachineParam, @Reading, @IsImage, @ImageType, @ImageUrl)";

            await using var command = new MySqlCommand(sql, connection);
            command.Parameters.AddWithValue("@LabNo", reading.LabNo);
            command.Parameters.AddWithValue("@MachineId", reading.MachineId);
            command.Parameters.AddWithValue("@MachineParam", reading.MachineParam);
            command.Parameters.AddWithValue("@Reading", reading.Reading);
            command.Parameters.AddWithValue("@IsImage", reading.IsImage);
            command.Parameters.AddWithValue("@ImageType", reading.ImageType ?? "");
            command.Parameters.AddWithValue("@ImageUrl", reading.ImageUrl ?? "");

            await command.ExecuteNonQueryAsync();
            _logger.Log($"Inserted: LabNo={reading.LabNo}, Param={reading.MachineParam}, Reading={reading.Reading}");
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError("Insert failed", ex);
            return false;
        }
    }

    /// <summary>
    /// Insert multiple readings in batch
    /// </summary>
    public async Task<bool> InsertMachineDataBatchAsync(List<MachineReading> readings)
    {
        if (readings.Count == 0) return true;

        try
        {
            await using var connection = new MySqlConnection(_connectionString);
            await connection.OpenAsync();

            foreach (var reading in readings)
            {
                const string sql = @"
                    INSERT INTO flabs_machinedata 
                    (LabNo, Machine_ID, Machine_Param, Reading, isImage, imageType, ImageUrl) 
                    VALUES (@LabNo, @MachineId, @MachineParam, @Reading, @IsImage, @ImageType, @ImageUrl)";

                await using var command = new MySqlCommand(sql, connection);
                command.Parameters.AddWithValue("@LabNo", reading.LabNo);
                command.Parameters.AddWithValue("@MachineId", reading.MachineId);
                command.Parameters.AddWithValue("@MachineParam", reading.MachineParam);
                command.Parameters.AddWithValue("@Reading", reading.Reading);
                command.Parameters.AddWithValue("@IsImage", reading.IsImage);
                command.Parameters.AddWithValue("@ImageType", reading.ImageType ?? "");
                command.Parameters.AddWithValue("@ImageUrl", reading.ImageUrl ?? "");

                await command.ExecuteNonQueryAsync();
                _logger.Log($"Inserted: LabNo={reading.LabNo}, Param={reading.MachineParam}, Reading={reading.Reading}");
            }

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError("Batch insert failed", ex);
            return false;
        }
    }

    /// <summary>
    /// Get machine observations for a LabNo (bidirectional - send to machine)
    /// </summary>
    public async Task<List<MachineObservation>> GetMachineObservationsAsync(string labNo)
    {
        var observations = new List<MachineObservation>();

        try
        {
            await using var connection = new MySqlConnection(_connectionString);
            await connection.OpenAsync();

            // First get data from flabs_data
            const string dataSql = "SELECT * FROM flabs_data WHERE LabNo = @LabNo";
            await using var dataCommand = new MySqlCommand(dataSql, connection);
            dataCommand.Parameters.AddWithValue("@LabNo", labNo);

            var dataResults = new List<(string LabObservationId, string PName, string Age, string Gender)>();
            
            await using (var reader = await dataCommand.ExecuteReaderAsync())
            {
                while (await reader.ReadAsync())
                {
                    dataResults.Add((
                        reader["LabObservation_ID"]?.ToString() ?? "",
                        reader["PName"]?.ToString() ?? "",
                        reader["Age"]?.ToString() ?? "",
                        reader["Gender"]?.ToString() ?? ""
                    ));
                }
            }

            // For each result, find the AssayNo from host mapping
            foreach (var data in dataResults)
            {
                const string mappingSql = @"
                    SELECT AssayNo FROM flabs_host_mapping 
                    WHERE LabObservation_ID = @LabObservationId AND MachineID = @MachineId";
                
                await using var mappingCommand = new MySqlCommand(mappingSql, connection);
                mappingCommand.Parameters.AddWithValue("@LabObservationId", data.LabObservationId);
                mappingCommand.Parameters.AddWithValue("@MachineId", _machineName);

                await using var mappingReader = await mappingCommand.ExecuteReaderAsync();
                if (await mappingReader.ReadAsync())
                {
                    observations.Add(new MachineObservation
                    {
                        LabNo = labNo,
                        AssayNo = mappingReader["AssayNo"]?.ToString() ?? "",
                        PatientName = data.PName,
                        Age = data.Age,
                        Gender = data.Gender
                    });
                }
            }

            _logger.Log($"GetObservations: LabNo={labNo}, Found={observations.Count} observations");
        }
        catch (Exception ex)
        {
            _logger.LogError($"GetObservations failed for LabNo={labNo}", ex);
        }

        return observations;
    }

    /// <summary>
    /// Get orders for machine (simplified query)
    /// </summary>
    public async Task<List<MachineObservation>> GetMachineOrdersAsync(string labNo)
    {
        var orders = new List<MachineObservation>();

        try
        {
            await using var connection = new MySqlConnection(_connectionString);
            await connection.OpenAsync();

            // Call stored procedure or direct query
            const string sql = @"
                SELECT hm.AssayNo, fd.PName as PatientName, fd.Age, fd.Gender
                FROM flabs_data fd
                INNER JOIN flabs_host_mapping hm ON fd.LabObservation_ID = hm.LabObservation_ID
                WHERE fd.LabNo = @LabNo AND hm.MachineID = @MachineId";

            await using var command = new MySqlCommand(sql, connection);
            command.Parameters.AddWithValue("@LabNo", labNo);
            command.Parameters.AddWithValue("@MachineId", _machineName);

            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                orders.Add(new MachineObservation
                {
                    LabNo = labNo,
                    AssayNo = reader["AssayNo"]?.ToString() ?? "",
                    PatientName = reader["PatientName"]?.ToString() ?? "",
                    Age = reader["Age"]?.ToString() ?? "",
                    Gender = reader["Gender"]?.ToString() ?? ""
                });
            }

            _logger.Log($"GetOrders: LabNo={labNo}, Found={orders.Count} orders");
        }
        catch (Exception ex)
        {
            _logger.LogError($"GetOrders failed for LabNo={labNo}", ex);
        }

        return orders;
    }
}
