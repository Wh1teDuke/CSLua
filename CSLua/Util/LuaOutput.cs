namespace CSLua.Util;

public static class LuaOutput
{
	// TODO Per LuaState output
	public static readonly Action<ReadOnlySpan<char>> WriteLine = Console.WriteLine;
	public static readonly Action<ReadOnlySpan<char>> Write = Console.Write;

	public static readonly Action<ReadOnlySpan<char>> ErrorWriteLine = Console.Error.WriteLine;
}