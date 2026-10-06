using System.IO;
using UnityEditor;

namespace HammerUnity.EditorTools
{
	/// <summary>
	/// Tools > Hammer > Debug Log: writes a trace of what the tool is doing to Logs/hammer-trace.log,
	/// flushed line by line so the last line shows where things stopped if the editor hangs.
	/// </summary>
	[InitializeOnLoad]
	static class HammerDebugLog
	{
		const string MenuPath = "Tools/Hammer/Debug Log";
		const string PrefKey = "HammerUnity.DebugLog";
		const string LogPath = "Logs/hammer-trace.log";

		static StreamWriter _writer;

		static HammerDebugLog()
		{
			if ( EditorPrefs.GetBool( PrefKey, false ) )
				Start();

			AssemblyReloadEvents.beforeAssemblyReload += Stop;
		}

		static void Start()
		{
			Directory.CreateDirectory( "Logs" );
			_writer = new StreamWriter( LogPath, true ) { AutoFlush = true };
			_writer.WriteLine( $"--- {System.DateTime.Now} ---" );
			HammerTrace.Sink = line =>
			{
				try { _writer?.WriteLine( $"{System.DateTime.Now:HH:mm:ss.fff} {line}" ); } catch { }
			};
		}

		static void Stop()
		{
			HammerTrace.Sink = null;
			_writer?.Dispose();
			_writer = null;
		}

		[MenuItem( MenuPath, false, 100 )]
		static void Toggle()
		{
			var on = !EditorPrefs.GetBool( PrefKey, false );
			EditorPrefs.SetBool( PrefKey, on );
			if ( on ) Start(); else Stop();
		}

		[MenuItem( MenuPath, true )]
		static bool ToggleValidate()
		{
			Menu.SetChecked( MenuPath, EditorPrefs.GetBool( PrefKey, false ) );
			return true;
		}
	}
}
