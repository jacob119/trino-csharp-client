using System.Data;
using Microsoft.Extensions.Configuration;
using Trino.Data.ADO.Server;

namespace Trino.Platform.Samples.Examples;

/// <summary>
/// 2. Connection String 방식 - 문자열 기반 접속 설정
/// </summary>
public static class ConnectionStringExample
{
    public static void Run(IConfiguration config)
    {
        Console.WriteLine("[2] Connection String 접속");
        Console.WriteLine();

        var section = config.GetSection("Trino");
        var host = section["Host"] ?? "localhost";
        var port = section["Port"] ?? "8080";
        var catalog = section["Catalog"] ?? "tpch";
        var schema = section["Schema"] ?? "tiny";
        var user = section["User"] ?? "trino-user";
        var enableSsl = bool.Parse(section["EnableSsl"] ?? "false");

        // Connection String 구성
        var connStr = $"host={host};port={port};catalog={catalog};schema={schema};user={user};enableSsl={enableSsl}";

        Console.WriteLine($"Connection String: {connStr}");
        Console.WriteLine();

        using var connection = new TrinoConnection();
        connection.ConnectionString = connStr;

        using var command = new TrinoCommand(
            connection,
            "SELECT name, regionkey FROM tpch.tiny.nation LIMIT 5");

        using var reader = command.ExecuteReader();

        for (int i = 0; i < reader.FieldCount; i++)
        {
            Console.Write($"{reader.GetName(i),-30}");
        }
        Console.WriteLine();
        Console.WriteLine(new string('-', 60));

        while (reader.Read())
        {
            for (int i = 0; i < reader.FieldCount; i++)
            {
                var value = reader.IsDBNull(i) ? "(null)" : reader.GetValue(i)?.ToString();
                Console.Write($"{value,-30}");
            }
            Console.WriteLine();
        }
    }
}
