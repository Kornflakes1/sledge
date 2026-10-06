// Small shims for APIs s&box gets from modern .NET that Unity's .NET Standard 2.1 profile lacks.
namespace Sandbox;

internal static class PolyfillExtensions
{
	public static int EnsureCapacity<T>( this List<T> list, int capacity )
	{
		if ( list.Capacity < capacity ) list.Capacity = capacity;
		return list.Capacity;
	}

	public static int EnsureCapacity<TKey, TValue>( this Dictionary<TKey, TValue> dict, int capacity ) => capacity;
	public static int EnsureCapacity<T>( this HashSet<T> set, int capacity ) => capacity;

	/// <summary>
	/// Stand-in for CollectionsMarshal.AsSpan. Copies, so writes don't reach the list.
	/// </summary>
	public static Span<T> AsSpanCopy<T>( this List<T> list ) => list.ToArray();

	public static float Float( this Random random ) => (float)random.NextDouble();
	public static float Float( this Random random, float max ) => (float)random.NextDouble() * max;
	public static float Float( this Random random, float min, float max ) => min + (float)random.NextDouble() * (max - min);
}

internal static class SharedRandom
{
	[ThreadStatic] static Random _instance;
	public static Random Shared => _instance ??= new Random();
}

/// <summary>
/// Stand-in for s&amp;box's Assert. Never compiled out: the ported code relies on the
/// argument expressions running (e.g. Assert.True( AddEdge( ... ) )).
/// </summary>
internal static class Assert
{
	public static void True( bool condition, string message = null )
	{
		if ( !condition ) throw new InvalidOperationException( message ?? "Assertion failed" );
	}

	public static void False( bool condition, string message = null )
	{
		if ( condition ) throw new InvalidOperationException( message ?? "Assertion failed" );
	}
}
