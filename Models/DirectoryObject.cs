using System;
using System.Collections.Generic;
using System.Text;

namespace S3FTPServer.Models
{
	class DirectoryObject
	{
		public string Name;
		public string Path;
		public ObjectType Type;

		public DirectoryObject(string _name, string _path, ObjectType _type)
		{
			Name = _name;
			Path = _path;
			Type = _type;
		}
	}

	public enum ObjectType
	{
		File,
		Directory
	}
}
