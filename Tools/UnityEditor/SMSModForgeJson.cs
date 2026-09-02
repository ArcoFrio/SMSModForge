// SMSModForge — the JSON writer the Unity extractors share.
//
// Hand-rolled because the trees these tools produce are arbitrary and Unity's
// JsonUtility only serialises fields on a serialisable class. It tracks whether
// a comma is needed, which is the one part of writing JSON by hand that is
// genuinely easy to get wrong.
//
// Public, not internal. Unity compiles anything under an Editor folder into
// Assembly-CSharp-Editor and everything else into Assembly-CSharp, and this file
// has no UnityEditor dependency, so it happily lands in either. Internal made
// that placement load-bearing: put this one file in a different folder from the
// extractors and they stop compiling, with an error about protection levels that
// says nothing about folders. Public costs nothing and removes the trap.
//
// It lives in its own file because it was compiled and round-tripped standalone
// before either extractor used it - including a run with the comma deliberately
// removed, to confirm the test could fail - and a second hand-rolled copy in the
// next tool would be a second copy of a solved problem. A misplaced comma here
// costs whoever runs these a full re-extraction in Unity.

using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;

namespace SMSModForge.EditorTools
{
    public sealed class Json
    {
        private readonly StringBuilder _sb = new StringBuilder();
        private readonly Stack<bool> _first = new Stack<bool>();
        private bool _afterKey;

        private void Separate()
        {
            if (_afterKey) { _afterKey = false; return; }
            if (_first.Count == 0) return;
            if (_first.Peek()) { _first.Pop(); _first.Push(false); }
            else _sb.Append(',');
        }

        public Json Object() { Separate(); _sb.Append('{'); _first.Push(true); return this; }
        public Json EndObject() { _sb.Append('}'); _first.Pop(); return this; }
        public Json Array() { Separate(); _sb.Append('['); _first.Push(true); return this; }
        public Json EndArray() { _sb.Append(']'); _first.Pop(); return this; }

        public Json Key(string name)
        {
            Separate();
            _sb.Append(JsonText.Quote(name)).Append(':');
            _afterKey = true;
            return this;
        }

        public Json Value(string v) { Separate(); _sb.Append(JsonText.Quote(v)); return this; }
        public Json Value(bool v) { Separate(); _sb.Append(v ? "true" : "false"); return this; }
        public Json Value(int v) { Separate(); _sb.Append(v.ToString(CultureInfo.InvariantCulture)); return this; }
        public Json Value(float v) { Separate(); _sb.Append(JsonText.F(v)); return this; }

        public Json Vector2(Vector2 v)
        {
            Separate();
            _sb.Append('[').Append(JsonText.F(v.x)).Append(',')
               .Append(JsonText.F(v.y)).Append(']');
            return this;
        }

        public Json Vector3(Vector3 v)
        {
            Separate();
            _sb.Append('[').Append(JsonText.F(v.x)).Append(',').Append(JsonText.F(v.y))
               .Append(',').Append(JsonText.F(v.z)).Append(']');
            return this;
        }

        public Json Vector4(Vector4 v)
        {
            Separate();
            _sb.Append('[').Append(JsonText.F(v.x)).Append(',').Append(JsonText.F(v.y))
               .Append(',').Append(JsonText.F(v.z)).Append(',')
               .Append(JsonText.F(v.w)).Append(']');
            return this;
        }

        public override string ToString() => _sb.ToString();
    }

    /// <summary>Number and string formatting for JSON, shared so the two
    /// extractors cannot drift apart on escaping or decimal separators - the
    /// invariant culture matters, since a machine set to a comma decimal would
    /// otherwise write coordinates that no parser accepts.</summary>
    public static class JsonText
    {
        public static string F(float v)
            => v.ToString("0.#####", CultureInfo.InvariantCulture);

        public static string Quote(string s)
        {
            if (s == null) return "\"\"";
            var sb = new StringBuilder(s.Length + 2);
            sb.Append('"');
            foreach (char ch in s)
            {
                switch (ch)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (ch < 0x20)
                            sb.Append("\\u").Append(((int)ch).ToString("x4", CultureInfo.InvariantCulture));
                        else sb.Append(ch);
                        break;
                }
            }
            sb.Append('"');
            return sb.ToString();
        }
    }
}
