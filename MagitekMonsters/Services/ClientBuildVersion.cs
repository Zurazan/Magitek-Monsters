namespace MagitekMonsters.Services;

public static class ClientBuildVersion
{
    public static string Current => typeof(ClientBuildVersion).Assembly.GetName().Version!.ToString();
}
