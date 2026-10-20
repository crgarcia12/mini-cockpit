using System;
using System.Collections.Generic;
using System.Text;

namespace MiniCockpitLab
{
    public sealed class PanelConnections
    {
        public bool Fcu { get; private set; }
        public bool Efis { get; private set; }
        public bool Overhead { get; private set; }

        public void Observe(string code)
        {
            if (code == "901") Fcu = true;
            else if (code == "956") Efis = true;
            else if (code == "957") Efis = false;
            else if (code == "958") Overhead = true;
            else if (code == "959") Overhead = false;
        }

        public void Reset() { Fcu = Efis = Overhead = false; }
    }

    public sealed class InputFrame
    {
        public string Raw { get; private set; }
        public string Code { get; private set; }
        public string Value { get; private set; }

        public static InputFrame Parse(string raw)
        {
            int separator = raw.IndexOf(',');
            bool numericPrefix = separator > 0;
            for (int i = 0; i < separator; i++)
                numericPrefix = numericPrefix && raw[i] >= '0' && raw[i] <= '9';
            return new InputFrame
            {
                Raw = raw,
                Code = numericPrefix ? raw.Substring(0, separator) : raw,
                Value = numericPrefix ? raw.Substring(separator + 1) : null
            };
        }

        public string FindMapping(IDictionary<string, string> mappings)
        {
            string label;
            if (mappings.TryGetValue(Raw, out label) && !String.IsNullOrWhiteSpace(label)) return label;
            return mappings.TryGetValue(Code, out label) && !String.IsNullOrWhiteSpace(label) ? label : null;
        }
    }

    public sealed class TokenParser
    {
        private readonly StringBuilder pending = new StringBuilder();

        public void Feed(string text, Action<string> receive)
        {
            foreach (char character in text)
            {
                if (character == ';')
                {
                    string token = pending.ToString().Trim();
                    pending.Clear();
                    if (token.Length > 0) receive(token);
                }
                else
                {
                    pending.Append(character);
                    if (pending.Length > 4096)
                        throw new FormatException("More than 4096 characters without a semicolon. Check baud rate and framing; raw bytes have been logged.");
                }
            }
        }

        public void Reset() { pending.Clear(); }
    }

    public static class Baseline
    {
        private static readonly Dictionary<string, string> Notes = new Dictionary<string, string>
        {
            { "1", "Heading control (old reference; direction/action unverified)" },
            { "2", "Heading control (old reference; direction/action unverified)" },
            { "3", "Heading control (old reference; direction/action unverified)" },
            { "4", "Heading control (old reference; direction/action unverified)" },
            { "11", "Speed push (old reference; unverified)" },
            { "12", "Speed pull (old reference; unverified)" },
            { "13", "Speed rotation (old reference; direction unverified)" },
            { "14", "Speed rotation (old reference; direction unverified)" },
            { "15", "Altitude control (old reference; action unverified)" },
            { "16", "Altitude control (old reference; action unverified)" },
            { "17", "Altitude control (old reference; action unverified)" },
            { "18", "Altitude control (old reference; action unverified)" },
            { "19", "Vertical speed control (old reference; action unverified)" },
            { "20", "Vertical speed control (old reference; action unverified)" },
            { "21", "Vertical speed control (old reference; action unverified)" },
            { "22", "Vertical speed control (old reference; action unverified)" },
            { "50", "AP1 (old reference; unverified)" },
            { "51", "AP2 (old reference; unverified)" },
            { "52", "Autothrust (old reference; unverified)" },
            { "53", "Localizer (old reference; unverified)" },
            { "55", "Approach (old reference; unverified)" },
            { "56", "Speed/Mach (old reference; unverified)" },
            { "57", "Heading/track (old reference; unverified)" },
            { "58", "Metric altitude (old reference; unverified)" },
            { "59", "Altitude increment selector (old reference; unverified)" },
            { "60", "Altitude increment selector (old reference; unverified)" },
            { "101", "BARO increase; payload is current pressure (DLS and hardware verified)" },
            { "102", "BARO decrease; payload is current pressure (DLS and hardware verified)" },
            { "901", "Controller connected (DLS connection status, not a physical control)" },
            { "956", "EFIS connected (DLS connection status, not a physical control)" },
            { "957", "EFIS disconnected (DLS connection status, not a physical control)" },
            { "958", "Overhead connected (DLS connection status, not a physical control)" },
            { "959", "Overhead disconnected (DLS connection status, not a physical control)" }
        };

        public static string Describe(string token)
        {
            string note;
            return Notes.TryGetValue(token, out note) ? note : "Unknown - capture and identify";
        }
    }
}
