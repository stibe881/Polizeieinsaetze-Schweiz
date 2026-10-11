using System.Collections.Generic;
using UnityEngine;

namespace SwissPoliceSim.Game
{
    public enum NoteKind { Info, Good, Warn, Bad, Radio }

    public sealed class Note { public string Text; public NoteKind Kind; public float Time; public float Duration = 6f; }

    /// <summary>Toast notifications shown by the HUD (and kept in the log for the notebook).</summary>
    public static class Notifier
    {
        public static readonly List<Note> Active = new List<Note>(); public static readonly List<string> Log = new List<string>();
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)] static void Reset() { Active.Clear(); Log.Clear(); }

        public static void Push(string text, NoteKind kind = NoteKind.Info, float duration = 6f)
        {
            Active.Add(new Note { Text = text, Kind = kind, Time = Time.unscaledTime, Duration = duration }); Log.Add(System.DateTime.Now.ToString("HH:mm") + "  " + text); if (Log.Count > 400) Log.RemoveAt(0);
            if (Active.Count > 6) Active.RemoveAt(0);
        }
        public static void Good(string t) => Push(t, NoteKind.Good); public static void Warn(string t) => Push(t, NoteKind.Warn, 8f); public static void Bad(string t) => Push(t, NoteKind.Bad, 10f); public static void Radio(string t) => Push(t, NoteKind.Radio, 12f);
        public static void Tick() { for (int i = Active.Count - 1; i >= 0; i--) if (Time.unscaledTime - Active[i].Time > Active[i].Duration) Active.RemoveAt(i); }
    }
}
