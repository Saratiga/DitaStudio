using Xunit;

namespace DitaStudio.UiTests;

/// <summary>Все классы этой коллекции делят один и тот же запущенный DitaStudio.exe (см.
/// <see cref="DitaStudioAppFixture"/>) — xUnit гарантирует, что тесты внутри одной коллекции не
/// выполняются параллельно друг с другом, что здесь обязательно (одно окно, одно состояние).</summary>
[CollectionDefinition("DitaStudio App")]
public sealed class AppCollection : ICollectionFixture<DitaStudioAppFixture>
{
}
