namespace HammerUnity
{
	/// <summary>
	/// Optional debug trace hook. Null (and free) unless something subscribes.
	/// </summary>
	public static class HammerTrace
	{
		public static System.Action<string> Sink;

		public static void Log( string message ) => Sink?.Invoke( message );
	}
}
