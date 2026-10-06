using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace HammerUnity.EditorTools
{
	/// <summary>
	/// Puts Hammer's settings (grid, bevel segments, toggles...) into Unity's undo history. Each
	/// change is copied into a hidden object that Undo records, and after an undo or redo whatever
	/// that object now holds is written back to the settings.
	/// </summary>
	sealed class SettingsUndo : ScriptableObject
	{
		[Serializable]
		sealed class Entry
		{
			public string Key;
			public string Type;
			public string Value; // null: not set (its default)
			public bool HasValue;
		}

		[SerializeField] List<Entry> _entries = new();

		static SettingsUndo _instance;

		/// <summary>
		/// True while settings are being put back, so that isn't recorded again.
		/// </summary>
		public static bool Applying { get; private set; }

		static SettingsUndo Instance
		{
			get
			{
				if ( _instance == null )
				{
					_instance = CreateInstance<SettingsUndo>();
					_instance.hideFlags = HideFlags.HideAndDontSave;
				}
				return _instance;
			}
		}

		[InitializeOnLoadMethod]
		static void Hook() => Undo.undoRedoPerformed += AfterUndo;

		static readonly Dictionary<string, (Func<string> Get, Action<string> Set)> _custom = new();

		/// <summary>
		/// Record a change to a value that isn't a stored setting (a shape's sides, a tool option),
		/// with how to read and write it as text.
		/// </summary>
		public static void RecordCustom( string key, string oldValue, string newValue, Func<string> get, Action<string> set )
		{
			_custom[key] = (get, set);
			Record( key, null, oldValue, newValue );
		}

		public static void Record( string key, System.Type type, string oldValue, string newValue )
		{
			var state = Instance;
			var entry = state._entries.Find( e => e.Key == key );

			// The value before the change has to be in the state before it's recorded
			if ( entry == null )
			{
				entry = new Entry { Key = key, Type = type?.FullName, Value = oldValue, HasValue = oldValue != null };
				state._entries.Add( entry );
			}
			else
			{
				entry.Value = oldValue;
				entry.HasValue = oldValue != null;
			}

			Undo.RecordObject( state, $"Change {ObjectNames.NicifyVariableName( key.Substring( key.LastIndexOf( '.' ) + 1 ) )}" );
			entry.Value = newValue;
			entry.HasValue = newValue != null;
		}

		static void AfterUndo()
		{
			if ( _instance == null ) return;

			var changed = false;
			Applying = true;
			try
			{
				foreach ( var e in _instance._entries )
				{
					if ( string.IsNullOrEmpty( e.Type ) ) // Unity stores a null string as empty
					{
						if ( !_custom.TryGetValue( e.Key, out var custom ) || custom.Get() == e.Value ) continue;
						custom.Set( e.Value );
						changed = true;
						continue;
					}

					var type = System.Type.GetType( e.Type ) ?? typeof( string );
					var value = e.HasValue ? e.Value : null;
					if ( HammerSettings.CurrentSetting( e.Key, type ) == value ) continue;
					HammerSettings.RestoreSetting( e.Key, type, value );
					changed = true;
				}
			}
			finally
			{
				Applying = false;
			}

			if ( changed ) HammerSettings.AfterSettingsUndo();
		}
	}
}
