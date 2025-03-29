namespace CSLua.Utils;

public static class ULDebug
{
	public static readonly Action<object> Log = Console.WriteLine;
	public static readonly Action<object> LogError = Console.Error.WriteLine;
}