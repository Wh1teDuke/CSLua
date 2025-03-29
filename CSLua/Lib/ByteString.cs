namespace CSLua.Lib;

internal sealed class ByteStringBuilder
{
	private readonly LinkedList<byte[]> _bufList = [];
	private int	_totalLength;
	
	public override string ToString()
	{
		if (_totalLength <= 0)
			return string.Empty;

		var result = new char[_totalLength];
		var node = _bufList.First;
		var i = 0;
		while (node != null)
		{
			var buf = node.Value;
			foreach (var t in buf) result[i++] = (char)t;
			node = node.Next;
		}
		return new string(result);
	}

	public ByteStringBuilder Append(byte[] bytes, int start, int length)
	{
		var buf = new byte[length];
		Array.Copy(bytes, start, buf, 0, length);
		_bufList.AddLast(buf);
		_totalLength += length;
		return this;
	}
}