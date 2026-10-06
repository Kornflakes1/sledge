// Polyfill so `init` accessors and records compile against Unity's .NET Standard 2.1 profile.
namespace System.Runtime.CompilerServices
{
	internal static class IsExternalInit { }
}
