using UnityEngine;
using SVector2 = Sandbox.Vector2;
using SVector3 = Sandbox.Vector3;
using SRotation = Sandbox.Rotation;
using STransform = Sandbox.Transform;

namespace HammerUnity
{
	/// <summary>
	/// Conversions between Unity space and the space the ported s&amp;box mesh code works in.
	/// <para>
	/// s&amp;box (like Source) is right-handed, Z-up, X-forward, Y-left and measured in inches.
	/// Unity is left-handed, Y-up, Z-forward, X-right and measured in metres. Mesh data is kept
	/// in s&amp;box space so the ported code (grid snapping, world-aligned texturing, primitives)
	/// behaves exactly like Hammer, and is converted only at the boundary.
	/// </para>
	/// </summary>
	public static class SourceSpace
	{
		/// <summary>
		/// Metres per s&amp;box unit. 1 unit = 1 inch, as in Hammer.
		/// </summary>
		public const float UnitScale = 0.0254f;

		public const float UnitsPerMetre = 1.0f / UnitScale;

		public static Vector3 ToUnityPosition( SVector3 v ) => new Vector3( -v.y, v.z, v.x ) * UnitScale;
		public static SVector3 ToSourcePosition( Vector3 v ) => new SVector3( v.z, -v.x, v.y ) * UnitsPerMetre;

		public static Vector3 ToUnityDirection( SVector3 v ) => new Vector3( -v.y, v.z, v.x );
		public static SVector3 ToSourceDirection( Vector3 v ) => new SVector3( v.z, -v.x, v.y );

		public static Quaternion ToUnityRotation( SRotation r )
		{
			// s&box forward (+X) maps to Unity +Z and up (+Z) to Unity +Y, matching LookRotation
			return Quaternion.LookRotation( ToUnityDirection( r.Forward ), ToUnityDirection( r.Up ) );
		}

		public static SRotation ToSourceRotation( Quaternion q )
		{
			var forward = ToSourceDirection( q * Vector3.forward );
			var up = ToSourceDirection( q * Vector3.up );
			return SRotation.LookAt( forward, up );
		}

		public static Vector2 ToUnityUV( SVector2 uv ) => new Vector2( uv.x, -uv.y );

		/// <summary>
		/// The s&amp;box-space world transform for a Unity transform. Used for world-aligned texturing.
		/// </summary>
		public static STransform ToSourceTransform( Transform t )
		{
			var scale = t.lossyScale;
			return new STransform(
				ToSourcePosition( t.position ),
				ToSourceRotation( t.rotation ),
				new SVector3( scale.z, scale.x, scale.y ) );
		}
	}
}
