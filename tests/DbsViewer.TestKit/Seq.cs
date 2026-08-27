using Xunit;

namespace DbsViewer.TestKit;

/// <summary>
/// Porovnání sekvencí. Existuje kvůli tomu, že <c>Assert.Equal</c> má přetížení pro
/// <c>ReadOnlySpan&lt;T&gt;</c> i pro <c>IEnumerable&lt;T&gt;</c> a u polí a kolekčních
/// výrazů je volání nejednoznačné.
/// </summary>
public static class Seq
{
    public static void Equal<T>(IEnumerable<T> expected, IEnumerable<T> actual) =>
        Assert.Equal(expected.ToList(), actual.ToList());
}
