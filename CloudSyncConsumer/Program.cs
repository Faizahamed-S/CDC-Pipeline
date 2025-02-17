using System;
using Confluent.Kafka;
using Newtonsoft.Json.Linq; // For parsing JSON
using Npgsql; // For cloud DB
using Prometheus;
using System.Threading;

namespace CloudSyncConsumer
{
    class Program
    {
         // Define counters at the class level so you can increment them inside the loop
        private static readonly Counter UpsertCounter = Metrics.CreateCounter(
            "cloud_db_upserts_total",
            "Number of upserts performed on the cloud database."
        );

        private static readonly Counter DeleteCounter = Metrics.CreateCounter(
            "cloud_db_deletes_total",
            "Number of deletes performed on the cloud database."
        );

        // (Optional) measure latency for each write
        private static readonly Histogram WriteLatency = Metrics.CreateHistogram(
            "cloud_db_write_latency_seconds",
            "Latency (in seconds) for upserts/deletes."
        );
        private static readonly Histogram CdcLatencyHistogram = Metrics.CreateHistogram(
            "cdc_latency_seconds",
            "End-to-end latency from local DB commit to cloud DB update."
        );
        static void Main(string[] args)
        {
            var metricServer = new MetricServer(port: 1234);
            metricServer.Start();
            // 1. Kafka Consumer Configuration
            var bootstrapServers = Environment.GetEnvironmentVariable("KAFKA_BOOTSTRAP_SERVERS") ?? "kafka:9092";
            Console.WriteLine("Using BootstrapServers: " + bootstrapServers);

            var consumerConfig = new ConsumerConfig
            {
                BootstrapServers = bootstrapServers,
                GroupId = "my-sync-group" + Guid.NewGuid().ToString(),
                AutoOffsetReset = AutoOffsetReset.Earliest,
            };
             // 3. Kafka Topic
            const string topicName = "local-postgres.public.mytable";

            // 4. Start Consumer
            using var consumer = new ConsumerBuilder<Ignore, string>(consumerConfig).Build();
            consumer.Subscribe(topicName);
            Console.WriteLine($"Subscribed to {topicName}. Waiting for messages...");

            // 2. Cloud DB Connection String
            var cloudDbConnectionString = Environment.GetEnvironmentVariable("CLOUD_DB_CONNECTION")
                ?? "Host=my-cloud-db.cpq4c26sgkao.us-east-2.rds.amazonaws.com;Port=5432;Username=postgres;Password=MyPassword123;Database=cloud_db";
            Console.WriteLine("Using cloud DB connection string: " + cloudDbConnectionString);


            try
            {
                while (true)
                {
                    var cr = consumer.Consume(); // blocking call
                    var messageValue = cr.Message.Value;
                    Console.WriteLine("Message received: " + messageValue);
                    if (string.IsNullOrEmpty(messageValue))
                    {
                        Console.WriteLine("Received an empty or null message. Skipping...");
                        continue; // Skip processing this message
                    }

                    // 5. Parse Debezium JSON
                    Console.WriteLine("debug1");
                    //var json = JObject.Parse(messageValue);
                    JObject json;
                        try
                        {
                            json = JObject.Parse(messageValue);
                        }
                        catch (Exception parseEx)
                        {
                            Console.WriteLine($"JSON parse error: {parseEx.Message}. Skipping message...");
                            continue; // Skip processing this malformed message
                        }
                    Console.WriteLine("debug2");
                    // var payload = json["payload"];
                    // Console.WriteLine("debug3");
                    // if (payload == null)
                    // {
                    //     Console.WriteLine("No payload in message");
                    //     continue;
                    // }

                    // Get operation type and record data
                    var op = (string)json["op"];
                    Console.WriteLine("Operation: " + op);

                    if (op == null)
                    {
                        Console.WriteLine("No op in message, skipping...");
                        continue;
                    }
                    // AFTER you parse 'json' and have 'op'
                    var source = json["source"];
                    if (source != null)
                    {
                        try
                        {
                            long sourceTsMs = (long)source["ts_ms"]; // local DB commit time in ms
                            var localDbCommitTime = DateTimeOffset.FromUnixTimeMilliseconds(sourceTsMs);
                            var now = DateTimeOffset.UtcNow;
                            var latency = now - localDbCommitTime;
                            double latencySeconds = latency.TotalSeconds;

                            // Observe the latency in our histogram
                            CdcLatencyHistogram.Observe(latencySeconds);

                            Console.WriteLine($"CDC latency: {latencySeconds:F3} seconds");
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"Failed to parse or record CDC latency: {ex.Message}");
                        }
                    }
                    else
                    {
                        Console.WriteLine("No 'source' object in the event, skipping latency measure...");
                    }

                    
                    var after = json["after"];
                    var before = json["before"];

                    if (op == "c" || op == "u")
                    {
                        try
                        {
                            // Parse 'after'
                            var id = (int)after["id"];
                            var name = (string)after["name"];
                            var desc = (string)after["description"];
                            Console.WriteLine($"Upsert attempt for id={id}, name={name}, description={desc}");

                            using var conn = new NpgsqlConnection(cloudDbConnectionString);
                            conn.Open();

                            var sql = @"
                                INSERT INTO mytable (id, name, description)
                                VALUES (@id, @name, @desc)
                                ON CONFLICT (id) 
                                DO UPDATE SET name = EXCLUDED.name,
                                              description = EXCLUDED.description;
                            ";
                            using var cmd = new NpgsqlCommand(sql, conn);
                            cmd.Parameters.AddWithValue("id", id);
                            cmd.Parameters.AddWithValue("name", (object)name ?? DBNull.Value);
                            cmd.Parameters.AddWithValue("desc", (object)desc ?? DBNull.Value);
                            int rowsAffected = cmd.ExecuteNonQuery();
                            Console.WriteLine($"Upsert executed for id={id}, rows affected: {rowsAffected}");
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"Upsert error: {ex.Message}");
                        }
                    }
                    else if (op == "d")
                    {
                        try
                        {
                            // Parse 'before'
                            var id = (int)before["id"];
                            Console.WriteLine($"Delete attempt for id={id}");

                            using var conn = new NpgsqlConnection(cloudDbConnectionString);
                            conn.Open();

                            var sql = "DELETE FROM mytable WHERE id = @id;";
                            using var cmd = new NpgsqlCommand(sql, conn);
                            cmd.Parameters.AddWithValue("id", id);
                            int rowsAffected = cmd.ExecuteNonQuery();
                            Console.WriteLine($"Delete executed for id={id}, rows affected: {rowsAffected}");
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"Delete error: {ex.Message}");
                        }
                    }
                    else
                    {
                        Console.WriteLine("Received unknown operation: " + op);
                    }

                    Console.WriteLine($"Processed op={op} for message offset={cr.Offset}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in consumer loop: {ex.Message}");
            }
            finally
            {
                consumer.Close();
            }
        }
    }
}
