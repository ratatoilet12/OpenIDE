using System;
using System.Collections.Generic;
using OpenIDENet.Languages;
namespace OpenIDENet.Projects
{
	public class Project : IProject
	{
		public string Fullpath { get; private set; }
		public object Content { get; private set; }
		public bool IsModified { get; private set; }
		
		public ProjectSettings Settings { get; private set; }
		
		public Project(string fullPath, string xml, ProjectSettings settings)
		{
			Fullpath = fullPath;
			Content = xml;
			Settings = settings;
		}
		
		public void SetContent(object content)
		{
			if (Content.Equals(content))
				return;
			IsModified = true;
			Content = content;
		}
	}
	
	public class ProjectSettings
	{
		public SupportedLanguage Type { get; private set; }
		public string DefaultNamespace { get; private set; }
		
		public ProjectSettings(SupportedLanguage type, string defaultNamespace)
		{
			Type = type;
			DefaultNamespace = defaultNamespace;
		}
	}	
}
