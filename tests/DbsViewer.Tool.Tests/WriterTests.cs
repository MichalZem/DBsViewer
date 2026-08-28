using DbsViewer.Analysis;
using DbsViewer.Dump;
using DbsViewer.TestKit;

namespace DbsViewer.Tests.DumpTool;

/// <summary>Textový výpis schématu a nálezů.</summary>
public class SchemaTextWriterTests
{
    private static DatabaseSchema Schema() => new()
    {
        DatabaseName = "Shop",
        Provider = DbProviderKind.Sqlite,
        SourceName = "test",
        Tables = [Build.Table("Orders", ["Id"], ["Id"])],
    };

    [Fact]
    public void Vypis_obsahuje_hlavicku_i_tabulky()
    {
        var output = SchemaTextWriter.Render(Schema());

        Assert.Contains("Databáze  : Shop", output, StringComparison.Ordinal);
        Assert.Contains("■ Orders", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Neznama_databaze_se_oznaci()
    {
        var output = SchemaTextWriter.Render(new DatabaseSchema());

        Assert.Contains("(neznámá)", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Migrace_se_vypisou_i_se_stavem()
    {
        var schema = Schema() with
        {
            Migrations =
            [
                new DbMigration { Id = "A", PresentInAssembly = true, AppliedInDatabase = true },
                new DbMigration { Id = "B", PresentInAssembly = true },
                new DbMigration { Id = "C", AppliedInDatabase = true },
            ],
        };

        var output = SchemaTextWriter.Render(schema);

        Assert.Contains("── Migrace ──", output, StringComparison.Ordinal);
        Assert.Contains("aplikovaná", output, StringComparison.Ordinal);
        Assert.Contains("čeká na nasazení", output, StringComparison.Ordinal);
        Assert.Contains("chybí v assembly", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Upozorneni_se_vypisou()
    {
        var schema = Schema() with { Warnings = ["něco se nepovedlo"] };

        var output = SchemaTextWriter.Render(schema);

        Assert.Contains("── Upozornění ──", output, StringComparison.Ordinal);
        Assert.Contains("! něco se nepovedlo", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Cisty_diff_to_rekne()
    {
        var diff = new SchemaDiff { Findings = [] };

        var output = SchemaTextWriter.RenderDiff(diff, Schema(), Schema());

        Assert.Contains("✓ Model a databáze se shodují", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Nalezy_se_vypisou_i_s_hodnotami()
    {
        var diff = new SchemaDiff
        {
            Findings =
            [
                new DiffFinding
                {
                    Kind = DiffKind.ColumnTypeMismatch,
                    Severity = DiffSeverity.Error,
                    Table = new DbObjectName(null, "Orders"),
                    Object = "Total",
                    Message = "Typ se liší.",
                    ModelValue = "decimal",
                    DatabaseValue = "money",
                },
                new DiffFinding
                {
                    Kind = DiffKind.IndexMissingInModel,
                    Severity = DiffSeverity.Warning,
                    Table = new DbObjectName(null, "Orders"),
                    Message = "Index navíc.",
                },
                new DiffFinding
                {
                    Kind = DiffKind.MigrationPending,
                    Severity = DiffSeverity.Info,
                    Message = "Informace.",
                },
            ],
        };

        var output = SchemaTextWriter.RenderDiff(diff, Schema(), Schema());

        Assert.Contains("── Chyby ──", output, StringComparison.Ordinal);
        Assert.Contains("── Varování ──", output, StringComparison.Ordinal);
        Assert.Contains("── Informace ──", output, StringComparison.Ordinal);
        Assert.Contains("model: decimal   databáze: money", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Nalez_bez_tabulky_se_oznaci_jako_schema()
    {
        var diff = new SchemaDiff
        {
            Findings =
            [
                new DiffFinding
                {
                    Kind = DiffKind.MigrationPending,
                    Severity = DiffSeverity.Error,
                    Message = "Migrace čeká.",
                },
            ],
        };

        Assert.Contains("(schéma)", SchemaTextWriter.RenderDiff(diff, Schema(), Schema()),
            StringComparison.Ordinal);
    }
}
