using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace HammerUnity.EditorTools
{
	/// <summary>
	/// Undo-able storage for a <see cref="MeshSelection"/>.
	/// </summary>
	public sealed class MeshSelectionState : ScriptableObject
	{
		[Serializable]
		public struct Entry
		{
			public HammerMesh Mesh;

			// The mesh's instance ID too: an object an edit deleted is a missing reference when the
			// selection is recorded, but undo brings it back under the same ID
			public int MeshId;
			public int Kind;
			public int Index;
		}

		public List<Entry> Entries = new();
	}

	/// <summary>
	/// The selected mesh elements, in selection order (some operations, like merging two edges,
	/// care about which came first). Changes are recorded in Unity's undo history.
	/// </summary>
	public sealed class MeshSelection : IEnumerable<IMeshElement>
	{
		readonly List<IMeshElement> _items = new();
		readonly HashSet<IMeshElement> _set = new();
		MeshSelectionState _state;

		public event Action Changed;

		public int Count => _items.Count;

		/// <summary>
		/// Goes up every time the selection changes.
		/// </summary>
		public int Version { get; private set; }

		public bool Contains( IMeshElement element ) => _set.Contains( element );

		MeshSelectionState State
		{
			get
			{
				if ( _state == null )
				{
					_state = ScriptableObject.CreateInstance<MeshSelectionState>();
					_state.hideFlags = HideFlags.HideAndDontSave;
				}

				return _state;
			}
		}

		// Recording the state copies all of it, so it's done once per operation rather than once
		// per element (selecting a thousand edges used to copy the list a thousand times, and redo
		// replayed every copy). Reset each editor tick, so separate actions still get their own.
		int _recordedGroup = -1;
		static int _tick;
		int _recordedTick = -1;

		static MeshSelection()
		{
			EditorApplication.update += () => _tick++;
		}

		/// <summary>
		/// Put the selection as it is now into the current undo step, before an edit changes it.
		/// An edit that deletes an object has to do this first: recorded after, the selection's
		/// references to that object are already gone and undo can't bring them back.
		/// </summary>
		/// <param name="force">Record again even if this step already has: needed after something
		/// part way through an edit (making or deleting an object) closed off the recording.</param>
		public void RecordNow( bool force = false ) => Record( force );

		void Record( bool force = false )
		{
			var group = Undo.GetCurrentGroup();
			if ( !force && group == _recordedGroup && _tick == _recordedTick )
				return;

			_recordedGroup = group;
			_recordedTick = _tick;
			Undo.RecordObject( State, "Selection" );
		}

		static MeshSelectionState.Entry ToEntry( IMeshElement x ) => new()
		{
			Mesh = x.Component,
			MeshId = x.Component != null ? x.Component.GetInstanceID() : 0,
			Kind = x is MeshVertex ? 0 : x is MeshEdge ? 1 : 2,
			Index = x.Index,
		};

		void Store()
		{
			State.Entries.Clear();
			foreach ( var x in _items )
				State.Entries.Add( ToEntry( x ) );
		}

		/// <summary>
		/// Rebuild from the stored state after an undo or redo.
		/// </summary>
		public void SyncFromUndo()
		{
			if ( _state == null ) return;

			_items.Clear();
			_set.Clear();

			foreach ( var stored in _state.Entries )
			{
				var e = stored;
				if ( e.Mesh == null && e.MeshId != 0 ) e.Mesh = EditorUtility.InstanceIDToObject( e.MeshId ) as HammerMesh;
				if ( e.Mesh == null ) continue;
				IMeshElement element = e.Kind switch
				{
					0 => new MeshVertex( e.Mesh, e.Index ),
					1 => MeshEdgeFromIndex( e.Mesh, e.Index ),
					_ => new MeshFace( e.Mesh, e.Index ),
				};

				if ( element != null && element.IsValid && _set.Add( element ) )
					_items.Add( element );
			}

			Version++;
			Changed?.Invoke();
		}

		static IMeshElement MeshEdgeFromIndex( HammerMesh mesh, int index )
		{
			var h = mesh.Mesh.HalfEdgeHandleFromIndex( index );
			return h.IsValid ? new MeshEdge( mesh, h ) : null;
		}

		public void Add( IMeshElement element )
		{
			if ( element is null || !element.IsValid || _set.Contains( element ) )
				return;

			Record();
			_set.Add( element );
			_items.Add( element );
			State.Entries.Add( ToEntry( element ) );
			Version++;
			Changed?.Invoke();
		}

		public void Remove( IMeshElement element )
		{
			if ( element is null || !_set.Contains( element ) )
				return;

			Record();
			_set.Remove( element );
			_items.Remove( element );
			Store();
			Version++;
			Changed?.Invoke();
		}

		public void Toggle( IMeshElement element )
		{
			if ( Contains( element ) ) Remove( element );
			else Add( element );
		}

		public void Set( IMeshElement element )
		{
			Record();
			_items.Clear();
			_set.Clear();

			if ( element is not null && element.IsValid )
			{
				_set.Add( element );
				_items.Add( element );
			}

			Store();
			Version++;
			Changed?.Invoke();
		}

		public void Clear()
		{
			if ( _items.Count == 0 )
				return;

			Record();
			_items.Clear();
			_set.Clear();
			Store();
			Version++;
			Changed?.Invoke();
		}

		/// <summary>
		/// Drop elements that no longer exist (deleted by an operation, undone, or the mesh was destroyed).
		/// </summary>
		public void RemoveInvalid()
		{
			var removed = _items.RemoveAll( x => x.Component == null || !x.IsValid );
			if ( removed == 0 )
				return;

			_set.Clear();
			foreach ( var x in _items ) _set.Add( x );
			Version++;
			Changed?.Invoke();
		}

		public IEnumerable<HammerMesh> Components => _items.Select( x => x.Component ).Where( x => x != null ).Distinct();

		public IEnumerator<IMeshElement> GetEnumerator() => _items.ToList().GetEnumerator();
		IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
	}

	/// <summary>
	/// Undo for mesh operations: records the meshes before the change and stores the result after.
	/// </summary>
	public readonly struct MeshUndoScope : IDisposable
	{
		readonly HammerMesh[] _components;

		public MeshUndoScope( string name, IEnumerable<HammerMesh> components )
		{
			_components = components.Where( x => x != null ).Distinct().ToArray();

			if ( _components.Length > 0 )
				Undo.RecordObjects( _components, name );
		}

		public void Dispose()
		{
			if ( _components is null )
				return;

			foreach ( var c in _components )
			{
				if ( c == null ) continue;
				c.Commit();
				EditorUtility.SetDirty( c );
			}

			// Warn straight away if the edit left broken faces behind
			MeshHealth.AfterEdit( _components );
		}
	}
}
