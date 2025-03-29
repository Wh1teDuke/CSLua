namespace CSLua;

public static class LuaFile
{
	public static FileLoadInfo OpenFile(string baseFolder, string filename)
	{
		var path = Path.Join(baseFolder, filename);
		return new FileLoadInfo(File.Open(
			path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite));
	}

	public static bool Readable(string baseFolder, string filename)
	{
		var path = Path.Join(baseFolder, filename);

		try
		{
			using (File.Open(
				    path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
				return true;
		}
		catch(Exception) { return false; }
	}
}

public readonly struct FileLoadInfo : ILoadInfo, IDisposable
{
	private readonly FileStream 	_stream;
	private readonly StreamReader 	_reader;
	private readonly Queue<char>	_buf;
	
	public FileLoadInfo(FileStream stream)
	{
		_stream = stream;
		_reader = new StreamReader(_stream, System.Text.Encoding.UTF8);
		_buf = new Queue<char>();
	}

	public int ReadByte() => 
		_buf.Count > 0 ? _buf.Dequeue() : _reader.Read();

	public int PeekByte()
	{
		if (_buf.Count > 0) return _buf.Peek();
		var c = _reader.Read();
		if (c == -1) return c;
		Save((char)c);
		return c;
	}

	public void Dispose()
	{
		_reader.Dispose();
		_stream.Dispose();
	}

	private void Save(char b) => _buf.Enqueue( b );

	private void Clear() => _buf.Clear();

	public void SkipComment()
	{
		var c = _reader.Read();//SkipBOM();

		// first line is a comment (Unix exec. file)?
		if (c == '#')
		{
			do { c = _reader.Read(); }
			while (c != -1 && c != '\n');
			Save('\n'); // fix line number
		}
		else if (c != -1)
		{
			Save((char)c);
		}
	}
}