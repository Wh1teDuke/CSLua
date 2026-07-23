namespace CSLua.Lib;

internal sealed class ByteStringBuilder
{
	private readonly List<byte[]> _bufList = [];
	private int	_totalLength;
	
	public override string ToString()
	{
		if (_totalLength <= 0) return string.Empty;

		var result = new char[_totalLength];
		var i = 0;
		
		foreach (var t in _bufList.SelectMany(buf => buf))
			result[i++] = (char)t;

		return new string(result);
	}

	public ByteStringBuilder Append(byte[] bytes, int start, int length)
	{
		var buf = new byte[length];
		Array.Copy(bytes, start, buf, 0, length);
		_bufList.Add(buf);
		_totalLength += length;
		return this;
	}
}