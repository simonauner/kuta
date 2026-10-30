using System.Xml.Linq;
using Dapper;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.AspNetCore.DataProtection.Repositories;
using Microsoft.Extensions.Options;
using Npgsql;

// Sparar Data Protection-nycklarna i Postgres så att cookies och antiforgery-token överlever omstarter.
// IXmlRepository är synkront, därav synkrona anrop.
public sealed class PostgresXmlRepository(NpgsqlDataSource db) : IXmlRepository
{
    public IReadOnlyCollection<XElement> GetAllElements()
    {
        using var conn = db.OpenConnection();
        return conn.Query<string>("select xml from data_protection_keys order by id").Select(XElement.Parse).ToList();
    }

    public void StoreElement(XElement element, string friendlyName)
    {
        using var conn = db.OpenConnection();
        conn.Execute("insert into data_protection_keys (friendly_name, xml) values (@friendlyName, @xml)",
            new { friendlyName, xml = element.ToString(SaveOptions.DisableFormatting) });
    }
}

public sealed class PostgresKeyManagementSetup(NpgsqlDataSource db) : IConfigureOptions<KeyManagementOptions>
{
    public void Configure(KeyManagementOptions options) => options.XmlRepository = new PostgresXmlRepository(db);
}
