using System;
using Side = Sandbox.Primitives.BlockPrimitive.Side;

namespace Sandbox.Primitives;

/// <summary>
/// Creates a wall section with a centered doorway and selectable outer faces.
/// </summary>
[Title( "Doorway" ), Icon( "door_front" )]
public sealed class DoorwayPrimitive : PrimitiveBuilder
{
	[Title( "Outer Faces" ), Editor( "horizontal-box-sides" ), WideMode, Description( "Choose which outer wall faces are created. The doorway jambs and inner arch are kept." )]
	public Side OuterSides { get; set; } = Side.All;

	[Title( "Door Width" ), Range( 1, 1024, slider: false ), WideMode, Description( "Width of the doorway opening." )]
	public float DoorWidth { get; set; } = 56.0f;

	[Title( "Door Height" ), Range( 1, 1024, slider: false ), WideMode, Description( "Height of the doorway opening." )]
	public float DoorHeight { get; set; } = 112.0f;

	[Title( "Arch Height" ), Range( 0, 512, slider: false ), WideMode, Description( "Height of the arch above the opening. Zero for a flat top." )]
	public float ArchHeight { get; set; } = 0.0f;

	[Title( "Arch Segments" ), Range( 2, 32, slider: false ), WideMode, Description( "Number of segments used to round the arch." )]
	public int ArchSegments { get; set; } = 8;

	[Title( "Align to Camera" ), WideMode, Description( "Rotate the wall so the doorway opening faces the camera." )]
	public bool AlignToCamera { get; set; } = true;

	[Hide] private BBox Bounds;

	public override void SetFromBox( BBox box ) => Bounds = box;

	public override void Build( PolygonMesh mesh )
	{
		const float minimumSectionSize = 0.01f;

		var size = Bounds.Size;
		var absoluteSize = new Vector3(
			MathF.Abs( size.x ),
			MathF.Abs( size.y ),
			MathF.Abs( size.z )
		);

		if ( absoluteSize.x <= minimumSectionSize ||
			absoluteSize.y <= minimumSectionSize ||
			absoluteSize.z <= minimumSectionSize )
		{
			return;
		}

		var swapAxes = AlignToCamera && ShouldSwapAxes();

		var x0 = Bounds.Mins.x;
		var x1 = Bounds.Maxs.x;
		var y0 = Bounds.Mins.y;
		var y1 = Bounds.Maxs.y;
		var z0 = Bounds.Mins.z;
		var z1 = Bounds.Maxs.z;

		if ( swapAxes )
		{
			(x0, y0) = (y0, x0);
			(x1, y1) = (y1, x1);
			size = new Vector3( size.y, size.x, size.z );
			absoluteSize = new Vector3(
				absoluteSize.y,
				absoluteSize.x,
				absoluteSize.z
			);
		}

		var maximumDoorWidth = MathF.Max(
			minimumSectionSize,
			absoluteSize.y - minimumSectionSize * 2.0f
		);

		var maximumDoorHeight = MathF.Max(
			minimumSectionSize,
			absoluteSize.z - minimumSectionSize
		);

		var openingWidth = Math.Clamp(
			DoorWidth,
			minimumSectionSize,
			maximumDoorWidth
		);

		var openingHeight = Math.Clamp(
			DoorHeight,
			minimumSectionSize,
			maximumDoorHeight
		);

		var centerY = (y0 + y1) * 0.5f;
		var halfWidth = openingWidth * 0.5f * MathF.Sign( size.y );
		var doorLeft = centerY - halfWidth;
		var doorRight = centerY + halfWidth;
		var doorTop = z0 + openingHeight * MathF.Sign( size.z );

		var maximumArchHeight = MathF.Max( 0.0f, openingHeight - minimumSectionSize );

		var archHeight = Math.Clamp( ArchHeight, 0.0f, maximumArchHeight );
		var hasArch = archHeight > 0.0f;
		var archSegments = hasArch
			? Math.Clamp( ArchSegments, 2, 32 )
			: 1;

		var springLine = doorTop - archHeight * MathF.Sign( size.z );

		var arch = new Vector3[archSegments + 1];
		for ( var i = 0; i <= archSegments; i++ )
		{
			var angle = MathF.PI * (i / (float)archSegments);
			var y = centerY - halfWidth * MathF.Cos( angle );
			var z = springLine + MathF.Sin( angle ) * archHeight * MathF.Sign( size.z );
			arch[i] = new Vector3( 0.0f, y, z );
		}

		arch[0] = new Vector3( 0.0f, doorLeft, springLine );
		arch[archSegments] = new Vector3( 0.0f, doorRight, springLine );

		void AddFace( params Vector3[] vertices )
		{
			if ( swapAxes )
			{
				for ( var i = 0; i < vertices.Length; i++ )
				{
					var v = vertices[i];
					vertices[i] = new Vector3( v.y, v.x, v.z );
				}
			}
			else
			{
				Array.Reverse( vertices );
			}

			mesh.AddFace( vertices );
		}

		void AddOuterFace( Side side, params Vector3[] vertices )
		{
			// Keep the face controls aligned with the box widget's axes after camera alignment.
			if ( swapAxes )
			{
				side = side switch
				{
					Side.Left => Side.Back,
					Side.Right => Side.Front,
					Side.Back => Side.Left,
					Side.Front => Side.Right,
					_ => side
				};
			}

			if ( (OuterSides & side) != 0 )
				AddFace( vertices );
		}

		// Front of wall.
		AddOuterFace( Side.Left,
			new Vector3( x0, y0, z0 ),
			new Vector3( x0, doorLeft, z0 ),
			new Vector3( x0, doorLeft, springLine ),
			new Vector3( x0, y0, springLine )
		);

		AddOuterFace( Side.Left,
			new Vector3( x0, y0, springLine ),
			new Vector3( x0, doorLeft, springLine ),
			new Vector3( x0, doorLeft, z1 ),
			new Vector3( x0, y0, z1 )
		);

		// With an arch the piece above the opening follows the curve. A flat top is just a single quad.

		if ( hasArch )
		{
			// Unity port: one face over the arch (top corners plus the curve) instead of a fan of
			// triangles, so it selects and textures as one piece like Hammer's
			var top = new List<Vector3> { new Vector3( x0, doorLeft, z1 ) };
			for ( var i = 0; i <= archSegments; i++ ) top.Add( new Vector3( x0, arch[i].y, arch[i].z ) );
			top.Add( new Vector3( x0, doorRight, z1 ) );
			AddOuterFace( Side.Left, top.ToArray() );
		}
		else
		{
			AddOuterFace( Side.Left,
				new Vector3( x0, doorLeft, springLine ),
				new Vector3( x0, doorRight, springLine ),
				new Vector3( x0, doorRight, z1 ),
				new Vector3( x0, doorLeft, z1 )
			);
		}

		AddOuterFace( Side.Left,
			new Vector3( x0, doorRight, z0 ),
			new Vector3( x0, y1, z0 ),
			new Vector3( x0, y1, springLine ),
			new Vector3( x0, doorRight, springLine )
		);

		AddOuterFace( Side.Left,
			new Vector3( x0, doorRight, springLine ),
			new Vector3( x0, y1, springLine ),
			new Vector3( x0, y1, z1 ),
			new Vector3( x0, doorRight, z1 )
		);

		// Back of wall.
		AddOuterFace( Side.Right,
			new Vector3( x1, doorLeft, z0 ),
			new Vector3( x1, y0, z0 ),
			new Vector3( x1, y0, springLine ),
			new Vector3( x1, doorLeft, springLine )
		);

		AddOuterFace( Side.Right,
			new Vector3( x1, doorLeft, springLine ),
			new Vector3( x1, y0, springLine ),
			new Vector3( x1, y0, z1 ),
			new Vector3( x1, doorLeft, z1 )
		);

		if ( hasArch )
		{
			var top = new List<Vector3> { new Vector3( x1, doorRight, z1 ) };
			for ( var i = archSegments; i >= 0; i-- ) top.Add( new Vector3( x1, arch[i].y, arch[i].z ) );
			top.Add( new Vector3( x1, doorLeft, z1 ) );
			AddOuterFace( Side.Right, top.ToArray() );
		}
		else
		{
			AddOuterFace( Side.Right,
				new Vector3( x1, doorRight, springLine ),
				new Vector3( x1, doorLeft, springLine ),
				new Vector3( x1, doorLeft, z1 ),
				new Vector3( x1, doorRight, z1 )
			);
		}

		AddOuterFace( Side.Right,
			new Vector3( x1, y1, z0 ),
			new Vector3( x1, doorRight, z0 ),
			new Vector3( x1, doorRight, springLine ),
			new Vector3( x1, y1, springLine )
		);

		AddOuterFace( Side.Right,
			new Vector3( x1, y1, springLine ),
			new Vector3( x1, doorRight, springLine ),
			new Vector3( x1, doorRight, z1 ),
			new Vector3( x1, y1, z1 )
		);

		// Outer left side.
		AddOuterFace( Side.Back,
			new Vector3( x1, y0, z0 ),
			new Vector3( x0, y0, z0 ),
			new Vector3( x0, y0, springLine ),
			new Vector3( x1, y0, springLine )
		);

		AddOuterFace( Side.Back,
			new Vector3( x1, y0, springLine ),
			new Vector3( x0, y0, springLine ),
			new Vector3( x0, y0, z1 ),
			new Vector3( x1, y0, z1 )
		);

		// Outer right side.
		AddOuterFace( Side.Front,
			new Vector3( x0, y1, z0 ),
			new Vector3( x1, y1, z0 ),
			new Vector3( x1, y1, springLine ),
			new Vector3( x0, y1, springLine )
		);

		AddOuterFace( Side.Front,
			new Vector3( x0, y1, springLine ),
			new Vector3( x1, y1, springLine ),
			new Vector3( x1, y1, z1 ),
			new Vector3( x0, y1, z1 )
		);

		// Top of wall.
		AddOuterFace( Side.Top,
			new Vector3( x0, y0, z1 ),
			new Vector3( x0, doorLeft, z1 ),
			new Vector3( x1, doorLeft, z1 ),
			new Vector3( x1, y0, z1 )
		);

		AddOuterFace( Side.Top,
			new Vector3( x0, doorLeft, z1 ),
			new Vector3( x0, doorRight, z1 ),
			new Vector3( x1, doorRight, z1 ),
			new Vector3( x1, doorLeft, z1 )
		);

		AddOuterFace( Side.Top,
			new Vector3( x0, doorRight, z1 ),
			new Vector3( x0, y1, z1 ),
			new Vector3( x1, y1, z1 ),
			new Vector3( x1, doorRight, z1 )
		);

		// Bottom of the two wall legs.
		AddOuterFace( Side.Bottom,
			new Vector3( x1, y0, z0 ),
			new Vector3( x1, doorLeft, z0 ),
			new Vector3( x0, doorLeft, z0 ),
			new Vector3( x0, y0, z0 )
		);

		AddOuterFace( Side.Bottom,
			new Vector3( x1, doorRight, z0 ),
			new Vector3( x1, y1, z0 ),
			new Vector3( x0, y1, z0 ),
			new Vector3( x0, doorRight, z0 )
		);

		// Left doorway jamb.
		AddFace(
			new Vector3( x0, doorLeft, z0 ),
			new Vector3( x1, doorLeft, z0 ),
			new Vector3( x1, doorLeft, springLine ),
			new Vector3( x0, doorLeft, springLine )
		);

		// Right doorway jamb.
		AddFace(
			new Vector3( x1, doorRight, z0 ),
			new Vector3( x0, doorRight, z0 ),
			new Vector3( x0, doorRight, springLine ),
			new Vector3( x1, doorRight, springLine )
		);

		// Inside of the arch, sweeping through the opening.
		for ( var i = 0; i < archSegments; i++ )
		{
			AddFace(
				new Vector3( x1, arch[i].y, arch[i].z ),
				new Vector3( x1, arch[i + 1].y, arch[i + 1].z ),
				new Vector3( x0, arch[i + 1].y, arch[i + 1].z ),
				new Vector3( x0, arch[i].y, arch[i].z )
			);
		}
	}

	/// <summary>
	/// The wall runs along Y by default, so it only needs turning when the
	/// camera is looking mostly down the Y axis.
	/// </summary>
	private static bool ShouldSwapAxes()
	{
		var forward = PrimitiveBuilder.CameraForward;
		return MathF.Abs( forward.y ) > MathF.Abs( forward.x );
	}
}
