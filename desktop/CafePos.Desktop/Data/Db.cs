using Microsoft.Data.Sqlite;
using System.IO;

namespace CafePos.Desktop.Data;

public static class Db
{
    public static string DataDir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        "CafePOS");

    public static string DbPath => Path.Combine(DataDir, "pos.sqlite");

    public static SqliteConnection Open()
    {
        Directory.CreateDirectory(DataDir);
        var cs = new SqliteConnectionStringBuilder
        {
            DataSource = DbPath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared
        }.ToString();

        var conn = new SqliteConnection(cs);
        conn.Open();
        return conn;
    }
}
