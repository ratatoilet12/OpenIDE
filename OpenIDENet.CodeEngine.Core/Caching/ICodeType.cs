using System;
namespace OpenIDENet.CodeEngine.Core
{
	public interface ICodeReference
	{
		string Type { get; }
		string File { get; }
		string Signature { get; }
		string Name { get; }
		int Offset { get; }
		int Line { get; }
		int Column { get; }
	}
	
	public class CodeReference : ICodeReference
	{
		public string Type { get; private set; }
		public string File { get; private set; }
		public string Signature { get; private set; }
		public string Name { get; private set; }
		public int Offset { get; private set; }
		public int Line { get; private set; }
		public int Column { get; private set; }

		public CodeReference(
			string type,
			string file,
			string signature,
			string name,
			int offset,
			int line,
			int column)
		{
			Type = type;
			File = file;
			Signature = signature;
			Name = name;
			Offset = offset;
			Line = line;
			Column = column;
		}
	}

	public class FilePosition
	{
		public string Fullpath { get; private set; }
		public int Line { get; private set; }
		public int Column { get; private set; }
		
		public FilePosition(string file, int line, int column)
		{
			Fullpath = file;
			Line = line;
			Column = column;
		}
	}
}

