using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using PolygonMesh = Sandbox.PolygonMesh;
using PolygonMeshData = Sandbox.PolygonMeshData;

namespace HammerUnity
{
	/// <summary>
	/// An editable polygon mesh, the Unity counterpart of s&amp;box's MeshComponent. Holds a
	/// <see cref="Sandbox.PolygonMesh"/> and bakes it into a regular Unity mesh and collider.
	/// </summary>
	[ExecuteAlways]
	[DisallowMultipleComponent]
	[RequireComponent( typeof( MeshFilter ), typeof( MeshRenderer ) )]
	[AddComponentMenu( "Hammer/Hammer Mesh" )]
	public sealed class HammerMesh : MonoBehaviour, ISerializationCallbackReceiver
	{
		public enum CollisionType
		{
			None,
			Mesh,
			Convex,
		}

		[Serializable]
		public struct MaterialBinding
		{
			public string Key;
			public UnityEngine.Material Material;
		}

		// The mesh, packed into one string (see PolygonMeshData.Pack). _data is the old layout,
		// still read from scenes saved before, and emptied once the mesh is saved again
		[SerializeField, HideInInspector] string _packed;
		[SerializeField, HideInInspector] PolygonMeshData _data = new();
		[SerializeField, HideInInspector] List<MaterialBinding> _materials = new();

		/// <summary>
		/// Changes whenever the stored data does. The baked meshes carry the revision they were
		/// baked from in their name, so a saved bake is only reused if it still matches.
		/// </summary>
		[SerializeField, HideInInspector] int _dataRevision;

		// Hammer's instances: meshes sharing this id are linked copies (edit one, they all change)
		[SerializeField, HideInInspector] string _instanceGroup;

		// Faces hidden while editing (H), kept with the scene. Only ever applied in edit mode:
		// in play mode and in builds every face shows
		[SerializeField, HideInInspector] int[] _hiddenFaces = System.Array.Empty<int>();

		/// <summary>
		/// The linked-instance group this mesh belongs to (null or empty: none). Copies of an
		/// instance (duplicates) keep the id and so stay linked.
		/// </summary>
		public string InstanceGroup
		{
			get => _instanceGroup;
			set => _instanceGroup = value;
		}

		[SerializeField] CollisionType _collision = CollisionType.Mesh;
		[SerializeField, Range( 0, 180 )] float _smoothingAngle;

		[Tooltip( "Smooth subdivision shown and collided with (the editable mesh stays as it is). Bake it in from the Hammer tool's Faces mode." )]
		[SerializeField, Range( 0, MaxSubdivision )] int _subdivisionLevel;

		public const int MaxSubdivision = 5;

		PolygonMesh _mesh;
		UnityEngine.Mesh _renderMesh;
		UnityEngine.Mesh _collisionMesh;
		bool _dataStale;
		bool _reloaded;
		Matrix4x4 _lastWorld;

		/// <summary>
		/// Raised after the Unity mesh is rebuilt. Editor tools use it to refresh overlays.
		/// </summary>
		public event Action Rebuilt;

		/// <summary>
		/// Raised after any Hammer mesh is rebuilt, so editor views can repaint.
		/// </summary>
		public static event Action<HammerMesh> AnyRebuilt;

		/// <summary>
		/// Rebuild now if undo, redo or a reload replaced the stored data since the last build.
		/// </summary>
		public void RebuildIfReloaded()
		{
			if ( !_reloaded ) return;
			_reloaded = false;
			RebuildUnityMesh();
		}

		public CollisionType Collision
		{
			get => _collision;
			set
			{
				if ( _collision == value ) return;
				_collision = value;
				RebuildUnityMesh();
			}
		}

		/// <summary>
		/// Catmull-Clark levels applied to what's drawn and collided with (0 = none).
		/// </summary>
		public int SubdivisionLevel
		{
			get => _subdivisionLevel;
			set
			{
				value = Mathf.Clamp( value, 0, MaxSubdivision );
				if ( _subdivisionLevel == value ) return;
				_subdivisionLevel = value;
				Commit();
			}
		}

		/// <summary>
		/// What's drawn: the mesh, smoothly subdivided if a level is set.
		/// </summary>
		int _builtSubdivision = -1;

#if UNITY_EDITOR
		// The level edited in the Inspector: show it (only when it actually changed, so loading
		// the scene doesn't rebuild everything)
		void OnValidate()
		{
			if ( _builtSubdivision < 0 || _builtSubdivision == _subdivisionLevel ) return;
			UnityEditor.EditorApplication.delayCall += () => { if ( this != null && _builtSubdivision != _subdivisionLevel ) RebuildUnityMesh(); };
		}
#endif

		PolygonMesh DisplayMesh( PolygonMesh mesh )
		{
			_builtSubdivision = _subdivisionLevel;
			if ( _subdivisionLevel <= 0 ) return mesh;
			var smooth = mesh.Subdivided( _subdivisionLevel );
			smooth.SetSmoothingAngle( 180 );
			smooth.Rebuild();
			return smooth;
		}

		public float SmoothingAngle
		{
			get => _smoothingAngle;
			set
			{
				if ( _smoothingAngle == value ) return;
				_smoothingAngle = value;
				Mesh.SetSmoothingAngle( value );
				Mesh.IsDirty = true;
			}
		}

		/// <summary>
		/// The editable mesh, in s&amp;box space (see <see cref="SourceSpace"/>). Set
		/// <see cref="Sandbox.PolygonMesh.IsDirty"/> or call <see cref="Commit"/> after changing it.
		/// </summary>
		public PolygonMesh Mesh
		{
			get
			{
				if ( _mesh is null )
				{
					RegisterMaterials();
					HammerTrace.Log( $"FromData {name} begin" );
					_mesh = PolygonMesh.FromData( LoadData() );
					HammerTrace.Log( $"FromData {name} end" );
					_mesh.SetSmoothingAngle( _smoothingAngle );
					_mesh.SetTransform( SourceSpace.ToSourceTransform( transform ) );
					if ( HidesFaces ) _mesh.SetHiddenFaceIndices( _hiddenFaces );
				}

				return _mesh;
			}
			set
			{
				_mesh = value ?? new PolygonMesh();
				_mesh.SetSmoothingAngle( _smoothingAngle );
				_mesh.SetTransform( SourceSpace.ToSourceTransform( transform ) );
				Commit();
			}
		}

		/// <summary>
		/// This object's transform in s&amp;box world space.
		/// </summary>
		public Sandbox.Transform WorldTransform => SourceSpace.ToSourceTransform( transform );

		/// <summary>
		/// Mesh-local (s&amp;box space) position to Unity world position.
		/// </summary>
		public Vector3 SourceToWorld( Sandbox.Vector3 local ) => transform.TransformPoint( SourceSpace.ToUnityPosition( local ) );

		/// <summary>
		/// Unity world position to mesh-local (s&amp;box space) position.
		/// </summary>
		public Sandbox.Vector3 WorldToSource( Vector3 world ) => SourceSpace.ToSourcePosition( transform.InverseTransformPoint( world ) );

		/// <summary>
		/// Mesh-local (s&amp;box space) direction to Unity world direction.
		/// </summary>
		public Vector3 SourceDirectionToWorld( Sandbox.Vector3 local ) => transform.TransformDirection( SourceSpace.ToUnityDirection( local ) );

		/// <summary>
		/// The baked Unity mesh used for rendering.
		/// </summary>
		public UnityEngine.Mesh RenderMesh => _renderMesh;

		/// <summary>
		/// Show a different polygon mesh without storing it, for interactive previews. Put the
		/// original back with <see cref="SetPreviewMesh"/> before recording undo and committing.
		/// </summary>
		public void SetPreviewMesh( PolygonMesh mesh )
		{
			_mesh = mesh ?? new PolygonMesh();
			_mesh.SetSmoothingAngle( _smoothingAngle );
			RebuildRenderMesh();
		}

		/// <summary>
		/// Mark the polygon mesh as edited during an interactive change (a drag or paint stroke).
		/// Unity compares an object against its undo snapshot whenever it serializes it, so the
		/// stored data has to follow along mid-drag or the change never makes it into undo.
		/// </summary>
		public void MarkModified()
		{
			_dataStale = true;
		}

		/// <summary>
		/// Rebuild the Unity mesh now and store the polygon mesh in the serialized data.
		/// </summary>
		public void Commit()
		{
			RebuildUnityMesh();
			_dataStale = true;
			FlushData();
		}

		// Which component each baked mesh belongs to, to spot a duplicated object still pointing
		// at the original's mesh
		static readonly Dictionary<UnityEngine.Mesh, HammerMesh> _owners = new();

		const string RenderSuffix = " (Hammer)";
		const string CollisionSuffix = " (Hammer Collision)";

		static readonly HashSet<HammerMesh> _enabled = new();

		/// <summary>
		/// Every enabled Hammer mesh (cheaper than searching the scene for them).
		/// </summary>
		public static IReadOnlyCollection<HammerMesh> Enabled => _enabled;

		/// <summary>
		/// Goes up whenever a mesh is enabled or disabled, so cached lists know they're stale.
		/// </summary>
		public static int EnabledVersion { get; private set; }

		void OnEnable()
		{
			if ( _enabled.Add( this ) ) EnabledVersion++;

			_lastWorld = transform.localToWorldMatrix;

			// The baked mesh was made in edit mode with the hidden faces left out; the game shows them
			if ( !HidesFaces && _hiddenFaces is { Length: > 0 } )
			{
				_mesh = null;
				RebuildUnityMesh();
				return;
			}

			// The baked meshes are saved with the scene. Opening the scene (or a script reload)
			// reuses them instead of rebuilding: rebuilding reassigns the MeshFilter and collider,
			// which marks the scene as changed and makes Unity ask to save it every time.
			if ( TryAdopt( GetComponent<MeshFilter>().sharedMesh, RenderSuffix, out _renderMesh ) )
			{
				var collider = GetComponent<MeshCollider>();
				if ( collider != null ) TryAdopt( collider.sharedMesh, CollisionSuffix, out _collisionMesh );

				_reloaded = false;

				// A material that wasn't an asset (the old made-up default) isn't saved with the
				// scene: put the materials back while editing
				if ( !Application.isPlaying && GetComponent<MeshRenderer>() is { } r && System.Array.Exists( r.sharedMaterials, m => m == null ) )
					RebuildUnityMesh();
				return;
			}

			RebuildUnityMesh();
		}

		bool TryAdopt( UnityEngine.Mesh mesh, string suffix, out UnityEngine.Mesh adopted )
		{
			adopted = null;
			// Ours, and baked from the data as it is now (not before an undo, say)
			if ( mesh == null || !mesh.name.EndsWith( suffix + Tag ) )
				return false;

			// Still owned by another live component: this object is a copy and needs its own (while
			// editing; in the game copies can share, and rebuilding there would need the editor)
			if ( !Application.isPlaying && _owners.TryGetValue( mesh, out var owner ) && owner != null && owner != this )
				return false;

			_owners[mesh] = this;
			adopted = mesh;
			return true;
		}

		string Tag => $" #{_dataRevision}";

		static bool HasArea( UnityEngine.Mesh mesh )
		{
			var v = mesh.vertices;
			var t = mesh.triangles;
			for ( int i = 0; i + 2 < t.Length; i += 3 )
				if ( Vector3.Cross( v[t[i + 1]] - v[t[i]], v[t[i + 2]] - v[t[i]] ).sqrMagnitude > 1e-12f )
					return true;
			return false;
		}

		/// <summary>
		/// Hidden faces only count while editing: not in play mode, not in a built game, and not
		/// while a build is processing the scene.
		/// </summary>
		static bool HidesFaces => Application.isEditor && !Application.isPlaying && !BuildingPlayer;

		/// <summary>Set by the editor while a build processes scenes.</summary>
		public static bool BuildingPlayer;

		/// <summary>
		/// For a build: put every hidden face back in the baked mesh (the scene copy being built,
		/// not the open one).
		/// </summary>
		public void ShowHiddenFacesForBuild()
		{
			if ( _hiddenFaces is not { Length: > 0 } ) return;
			_mesh = null;
			RebuildUnityMesh();

			// The built copy is baked with every face, so the game has nothing to rebuild
			_hiddenFaces = Array.Empty<int>();
		}

		void Claim( UnityEngine.Mesh mesh )
		{
			if ( mesh != null ) _owners[mesh] = this;
		}

		void OnDisable()
		{
			if ( _enabled.Remove( this ) ) EnabledVersion++;

			// The meshes stay: they belong to the scene now. Just let go of them.
			if ( _renderMesh != null && _owners.TryGetValue( _renderMesh, out var a ) && a == this ) _owners.Remove( _renderMesh );
			if ( _collisionMesh != null && _owners.TryGetValue( _collisionMesh, out var b ) && b == this ) _owners.Remove( _collisionMesh );
		}

		void Update()
		{
			if ( Application.isPlaying )
				return;

			if ( _reloaded )
			{
				_reloaded = false;
				RebuildUnityMesh();
				return;
			}

			if ( _mesh is null )
				return;

			// Keep world-aligned texture parameters in sync with the transform, like s&box does
			// on TransformChanged. This doesn't touch the texture coordinates themselves.
			var world = transform.localToWorldMatrix;
			if ( world != _lastWorld )
			{
				_lastWorld = world;
				var wasDirty = _mesh.IsDirty;
				_mesh.Transform = SourceSpace.ToSourceTransform( transform );
				_mesh.IsDirty = wasDirty;
			}

			if ( _mesh.IsDirty )
			{
				Commit();
			}
			else if ( _mesh.IsVertexDataDirty )
			{
				_mesh.UpdateVertexData();
				RebuildUnityMesh( rebuildTopology: false );
				_dataStale = true;
				FlushData();
			}
		}

		/// <summary>
		/// Triangulate the polygon mesh and upload it to the MeshFilter and collider.
		/// </summary>
		public void RebuildUnityMesh() => RebuildUnityMesh( rebuildTopology: true );

		/// <summary>
		/// Re-bake just the render mesh, for interactive edits. Collision is rebuilt on <see cref="Commit"/>;
		/// cooking a mesh collider every frame of a drag is slow.
		/// </summary>
		public void RebuildRenderMesh() => RebuildUnityMesh( rebuildTopology: true, updateCollider: false );

		void RebuildUnityMesh( bool rebuildTopology, bool updateCollider = true )
		{
			HammerTrace.Log( $"RebuildUnityMesh {name}" );
			var mesh = Mesh;

			if ( rebuildTopology )
			{
				HammerTrace.Log( "  Rebuild begin" );
				mesh.Rebuild();
				HammerTrace.Log( "  Rebuild end" );
			}

			if ( _renderMesh == null )
			{
				// Saved with the scene (see OnEnable)
				_renderMesh = new UnityEngine.Mesh { name = name + RenderSuffix };
				_renderMesh.indexFormat = IndexFormat.UInt32;
				Claim( _renderMesh );
			}

			var display = DisplayMesh( mesh );
			var materials = MeshBaker.Bake( display, _renderMesh );
			_renderMesh.name = name + RenderSuffix + Tag;
			HammerTrace.Log( "  Bake end" );

			// Only touch the components when something's different: every assignment counts as
			// a change to the scene
			var filter = GetComponent<MeshFilter>();
			if ( filter.sharedMesh != _renderMesh ) filter.sharedMesh = _renderMesh;
			var renderer = GetComponent<MeshRenderer>();
			if ( !SameMaterials( renderer.sharedMaterials, materials ) ) renderer.sharedMaterials = materials;

			if ( updateCollider )
			{
				UpdateCollider( display );
				HammerTrace.Log( "  Collider end" );
			}

			Rebuilt?.Invoke();
			AnyRebuilt?.Invoke( this );
		}

		void UpdateCollider( PolygonMesh mesh )
		{
			var collider = GetComponent<MeshCollider>();

			if ( _collision == CollisionType.None )
			{
				if ( collider != null && collider.enabled )
					collider.enabled = false;

				return;
			}

			if ( collider == null )
			{
				collider = gameObject.AddComponent<MeshCollider>();
				collider.hideFlags = HideFlags.None;
			}

			if ( _collisionMesh == null )
			{
				_collisionMesh = new UnityEngine.Mesh { name = name + CollisionSuffix };
				_collisionMesh.indexFormat = IndexFormat.UInt32;
				Claim( _collisionMesh );
			}

			MeshBaker.BakeCollision( mesh, _collisionMesh );
			_collisionMesh.name = name + CollisionSuffix + Tag;

			if ( !collider.enabled ) collider.enabled = true;
			var convex = _collision == CollisionType.Convex;
			if ( collider.convex != convex ) collider.convex = convex;
			// Reassigning makes the collider re-cook the changed mesh
			collider.sharedMesh = null;
			// PhysX refuses (with an error) a mesh whose triangles all have no area
			collider.sharedMesh = _collisionMesh.vertexCount >= 3 && HasArea( _collisionMesh ) ? _collisionMesh : null;
		}

		static bool SameMaterials( UnityEngine.Material[] a, UnityEngine.Material[] b )
		{
			if ( a == null || b == null || a.Length != b.Length ) return false;
			for ( int i = 0; i < a.Length; i++ )
				if ( a[i] != b[i] ) return false;
			return true;
		}

		void RegisterMaterials()
		{
			foreach ( var binding in _materials )
			{
				if ( !string.IsNullOrEmpty( binding.Key ) && binding.Material != null )
					HammerMaterials.Register( binding.Key, binding.Material );
			}
		}

		/// <summary>
		/// The saved mesh data: packed if there is any, otherwise the old layout.
		/// </summary>
		PolygonMeshData LoadData() => !string.IsNullOrEmpty( _packed ) ? PolygonMeshData.Unpack( _packed ) : _data;

		/// <param name="rename">Also update the baked meshes' revision tags. Not done from the
		/// serialization callback, where touching other objects isn't allowed; every edit ends in
		/// <see cref="Commit"/>, which does it.</param>
		void FlushData( bool rename = true )
		{
			if ( !_dataStale || _mesh is null )
				return;

			var data = _mesh.ToData();
			_packed = data.Pack();
			if ( HidesFaces ) _hiddenFaces = _mesh.HiddenFaceIndices;
			_data = new PolygonMeshData();
			_dataRevision = UnityEngine.Random.Range( 1, int.MaxValue );

			if ( rename )
			{
				if ( _renderMesh != null ) _renderMesh.name = name + RenderSuffix + Tag;
				if ( _collisionMesh != null ) _collisionMesh.name = name + CollisionSuffix + Tag;
			}

			_materials.Clear();
			foreach ( var key in data.Materials )
			{
				if ( string.IsNullOrEmpty( key ) ) continue;
				_materials.Add( new MaterialBinding { Key = key, Material = HammerMaterials.Resolve( key ) } );
			}

			_dataStale = false;
		}

		public void OnBeforeSerialize()
		{
			HammerTrace.Log( $"OnBeforeSerialize stale={_dataStale}" );
			FlushData( rename: false );
		}

		public void OnAfterDeserialize()
		{
			HammerTrace.Log( "OnAfterDeserialize" );
			// Undo, paste or a domain reload replaced the serialized data; reload from it.
			_mesh = null;
			_dataStale = false;
			_reloaded = true;

#if UNITY_EDITOR
			// Edit mode Update only runs when something else changes, so rebuild on the next editor tick
			UnityEditor.EditorApplication.delayCall += () =>
			{
				if ( this != null && _reloaded && !Application.isPlaying )
				{
					_reloaded = false;
					RebuildUnityMesh();
				}
			};
#endif
		}

		static void DestroySafe( UnityEngine.Object o )
		{
			if ( Application.isPlaying ) Destroy( o );
			else DestroyImmediate( o );
		}
	}
}
