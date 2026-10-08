using System;
using System.Diagnostics;

namespace HammerUnity.EditorTools
{
	/// <summary>
	/// Timing: <c>using ( HammerPerf.Time( "name" ) ) { ... }</c> prints to the Console when the
	/// block takes longer than <see cref="Threshold"/> ms. Off unless the EditorPrefs bool
	/// "HammerUnity.Perf" is set.
	/// </summary>
	public static class HammerPerf
	{
		public const double Threshold = 4.0;

		static bool? _enabled;
		static bool Enabled => _enabled ??= UnityEditor.EditorPrefs.GetBool( "HammerUnity.Perf", false );

		public readonly struct Scope : IDisposable
		{
			readonly string _name;
			readonly long _start;

			public Scope( string name )
			{
				_name = name;
				_start = Enabled ? Stopwatch.GetTimestamp() : 0;
			}

			public void Dispose()
			{
				if ( _start == 0 ) return;
				var ms = (Stopwatch.GetTimestamp() - _start) * 1000.0 / Stopwatch.Frequency;
				if ( ms >= Threshold )
					UnityEngine.Debug.Log( $"[HammerPerf] {_name}: {ms:0.0} ms" );
			}
		}

		public static Scope Time( string name ) => new( name );
	}
}
