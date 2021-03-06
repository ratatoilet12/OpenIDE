using System;
using System.Collections;
using System.IO;
using System.Collections.Generic;
using OpenIDE.CodeEngine.Core.Caching;
using OpenIDE.Core.Logging;
using OpenIDE.CodeEngine.Core.Endpoints;
using System.Linq;
using OpenIDE.Core.Language;
using OpenIDE.Core.Caching;
using System.Reflection;
using OpenIDE.Core.Profiles;

namespace OpenIDE.CodeEngine.Core.ChangeTrackers
{
	public class PluginFileTracker : IDisposable
	{
		private EventEndpoint _eventDispatcher;
		private List<PluginPattern> _plugins = new List<PluginPattern>();
		private FileChangeTracker _tracker;
		private FileChangeTracker _localTracker;
		private FileChangeTracker _globalTracker;
		private ICacheBuilder _cache;
		private ICrawlResult _crawlReader;
		
		public void Start(
			string path,
			ICacheBuilder cache,
			ICrawlResult crawlReader,
			PluginLocator pluginLocator,
			EventEndpoint eventDispatcher,
			string[] ignoreDirectories)
		{
			_cache = cache;
			_crawlReader = crawlReader;
			_eventDispatcher = eventDispatcher;
			Logger.Write("Setting up file trackers");
			Logger.Write("Setting up token file trackers");
			_tracker = new FileChangeTracker((x) => {
					if (x.Path.StartsWith(Path.Combine(path, ".OpenIDE")))
						return;
					_eventDispatcher.Send(
						"codemodel raw-filesystem-change-" +
						x.Type.ToString().ToLower() +
						" \"" + x.Path + "\"");
				});
			Logger.Write("Setting up local file trackers");
			_localTracker = new FileChangeTracker((x) => {
					_eventDispatcher.Send(
						"codemodel raw-filesystem-change-" +
						x.Type.ToString().ToLower() +
						" \"" + x.Path + "\"");
				});
			Logger.Write("Setting up global file trackers");
			_globalTracker = new FileChangeTracker((x) => {
					_eventDispatcher.Send(
						"codemodel raw-filesystem-change-" +
						x.Type.ToString().ToLower() +
						" \"" + x.Path + "\"");
				});
			Logger.Write("Adding plugins to cache");
			var plugins = pluginLocator.Locate().ToList();
			foreach (var x in plugins) {
				var plugin = new PluginPattern(x);
				_plugins.Add(plugin);
				_cache.Plugins.Add(
					new CachedPlugin(x.GetLanguage(), plugin.Patterns));
				Logger.Write("Added plugin " + x.GetLanguage());
			}	
			var locator = new ProfileLocator(path);
            var profilePath = locator.GetLocalProfilePath(locator.GetActiveLocalProfile());
			if (Directory.Exists(profilePath)) {
                Logger.Write("Starting tracker for {0}", path);
                _tracker.Start(path, getFilter(), handleChanges, ignoreDirectories);
            } else {
                Logger.Write("No local configuration point so not starting file tracker");
            }
			if (Environment.OSVersion.Platform == PlatformID.Unix || Environment.OSVersion.Platform == PlatformID.MacOSX) {
				if (Directory.Exists(profilePath)) {
					Logger.Write("Starting tracker for {0}", profilePath);
					_localTracker.Start(profilePath, getFilter(), handleChanges, ignoreDirectories);
				}
			}
			var globalPath = locator.GetGlobalProfilePath(locator.GetActiveGlobalProfile());
			if (Directory.Exists(globalPath)) {
				Logger.Write("Starting tracker for {0}", globalPath);
				_globalTracker.Start(globalPath, getFilter(), handleChanges, ignoreDirectories);
			}
		}

		private string getFilter()
		{
			var pattern = "";
			_plugins
				.ForEach(x => 
					{
						x.Patterns
							.ForEach(y =>
								{
									if (pattern == "")
										pattern = "*" + y;
									else
										pattern += "|*" + y;
								});
					});
			return pattern;
		}
			               
		private void handleChanges(Stack<Change> buffer)
		{
			var cacheHandler = new CrawlHandler(_crawlReader, (s) => Logger.Write(s));
			var files = getChanges(buffer);
			files.ForEach(x =>
				{
					_cache.Invalidate(x.Path);
					handle(x);
				});
			foreach (var plugin in _plugins)
				plugin.Handle(cacheHandler);
		}
		
		private List<Change> getChanges(Stack<Change> buffer)
		{
			var list = new List<Change>();
			while (buffer.Count != 0)
			{
				var item = buffer.Pop();
				if (item != null && !list.Exists(x => x.Path.Equals(item.Path)))
					list.Add(item);
			}
			return list;
		}
		
		private void handle(Change file)
		{
			if (file == null)
				return;
			var extension = Path.GetExtension(file.Path).ToLower();
			if (extension == null)
				return;
			
			try {
				_eventDispatcher.Send(
					"codemodel filesystem-change-" +
					file.Type.ToString().ToLower() +
					" \"" + file.Path + "\"");
				
				_plugins.ForEach(x =>
					{
						if (x.Supports(extension) && !x.FilesToHandle.Contains(file.Path))
							x.FilesToHandle.Add(file.Path);
				 	});

				_eventDispatcher.Send("codemodel file-crawled \"" + file.Path + "\"");
			} catch (Exception ex) {
				Logger.Write("Failed while handling file system changes. CodeModel may be out of sync");
				Logger.Write(ex);
			}
		}

		public void Dispose()
		{
			_tracker.Dispose();
			_localTracker.Dispose();
			_globalTracker.Dispose();
		}
	}

	class PluginPattern
	{
		public LanguagePlugin Plugin { get; private set; }
		public List<string> Patterns { get; private set; }
		public List<string> FilesToHandle { get; private set; }

		public PluginPattern(LanguagePlugin plugin)
		{
			Plugin = plugin;
			Patterns = new List<string>();
			Logger.Write("Getting file types for " + plugin.FullPath);
			Patterns.AddRange(
				Plugin
					.GetCrawlFileTypes()
						.Split(new string[] { "|" }, StringSplitOptions.RemoveEmptyEntries));
			FilesToHandle = new List<string>();
		}

		public bool Supports(string extension)
		{
			return Patterns.Count(x => x.ToLower().Equals(extension)) > 0;
		}

		public void Handle(CrawlHandler cacheHandler)
		{
			if (FilesToHandle.Count == 0)
				return;
            try {
                cacheHandler.SetLanguage(Plugin.GetLanguage());
                Plugin.Crawl(FilesToHandle, (line) => cacheHandler.Handle(line));
                FilesToHandle.Clear();
            } catch (Exception ex) {
                Logger.Write(ex);
            }
		}
	}
}

