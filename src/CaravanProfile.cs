using System;
using System.Collections.Generic;
using System.Globalization;

namespace MiniCockpitLab
{
    public static class AircraftProfiles
    {
        public const string Automatic = "Auto-detect (MSFS)";
        public const string Caravan = "Cessna 208B Grand Caravan";

        public static string Detect(string title)
        {
            if (String.IsNullOrWhiteSpace(title)) return null;
            string name = title.ToUpperInvariant().Replace(" ", "").Replace("-", "").Replace("_", "");
            if (name.Contains("C208") || name.Contains("208B") || name.Contains("CARAVAN")) return Caravan;
            if (name.Contains("A321")) return "Airbus A321neo";
            if (name.Contains("A320") || name.Contains("A32NX")) return "Airbus A320neo";
            if (name.Contains("A330900") || name.Contains("A330NEO") || name.Contains("A339")) return "Airbus A330-900";
            return null;
        }
    }

    public sealed class SimulatorState
    {
        public string Aircraft;
        public DateTime ReceivedUtc;
        public bool Autopilot;
        public bool AltitudeHold;
        public bool FlightLevelChange;
        public double Speed;
        public double Altitude;
        public double Heading;
        public double VerticalSpeed;
        public bool HeadingHold;
        public bool NavigationHold;
        public bool VerticalHold;
        public bool Strobe;
        public bool Beacon;
        public bool Navigation;
        public bool Taxi;
        public bool Landing;
        public bool OnGround;

        public bool IsCaravan
        {
            get
            {
                return AircraftProfiles.Detect(Aircraft) == AircraftProfiles.Caravan;
            }
        }
    }

    public sealed class SimulatorCommand
    {
        public string Event;
        public uint Value;
        public SimulatorCommand(string name, uint value) { Event = name; Value = value; }
    }

    public sealed class CaravanProfile
    {
        private bool strobeAuto;
        private bool runwayLight;
        private bool noseLight;

        public void Reset()
        {
            strobeAuto = runwayLight = noseLight = false;
        }

        public IEnumerable<SimulatorCommand> Handle(InputFrame input, SimulatorState state)
        {
            if (state == null || !state.IsCaravan)
                throw new InvalidOperationException("Load the Cessna 208 Caravan in MSFS before controlling it.");
            string code = input.Code;
            if (code == "50") return One("AP_MASTER", 0);
            if (code == "51") return One("AP_NAV1_HOLD", 0);
            if (code == "1" || code == "2") return One("AP_PANEL_HEADING_ON", 0);
            if (code == "15" || code == "16") return One("AP_ALT_HOLD_ON", 0);
            if (code == "11" || code == "12") return One("FLIGHT_LEVEL_CHANGE_ON", 0);
            if (code == "3" || code == "4")
                return SetOrStep(input, "HEADING_BUG_SET", code == "3" ? "HEADING_BUG_INC" : "HEADING_BUG_DEC", 0, 0, 360);
            if (code == "13" || code == "14")
            {
                if (!state.FlightLevelChange)
                    throw new InvalidOperationException("SPD controls the Caravan FLC target. Push/pull SPD to engage FLC first.");
                return SetOrStep(input, "AP_SPD_VAR_SET", code == "13" ? "AP_SPD_VAR_INC" : "AP_SPD_VAR_DEC", 1, 0, 250);
            }
            if (code == "17" || code == "18")
            {
                uint step = 100;
                if (input.Value != null)
                {
                    uint value;
                    if (!UInt32.TryParse(input.Value, NumberStyles.None, CultureInfo.InvariantCulture, out value) || value > 30000)
                        throw new ArgumentException("Invalid Caravan altitude input: " + input.Raw + ".");
                    if (Math.Abs(value - state.Altitude) == 1000) step = 1000;
                }
                // NXi transforms absolute SET events into knob increments; use the knob events directly.
                return One(code == "17" ? "AP_ALT_VAR_INC" : "AP_ALT_VAR_DEC", step);
            }
            if (code == "21" || code == "22")
            {
                if (input.Value == null) return One(code == "21" ? "AP_VS_VAR_INC" : "AP_VS_VAR_DEC", 0);
                int verticalSpeed;
                if (!Int32.TryParse(input.Value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out verticalSpeed)
                    || verticalSpeed < -6000 || verticalSpeed > 6000)
                    throw new ArgumentException("Invalid Caravan vertical-speed target: " + input.Raw + ".");
                return One("AP_VS_VAR_SET_ENGLISH", unchecked((uint)verticalSpeed));
            }
            if (code == "212" || code == "213" || code == "214")
            {
                strobeAuto = code == "214";
                return One("STROBES_SET", code == "213" || (strobeAuto && !state.OnGround) ? 1u : 0u);
            }
            if (code == "215" || code == "216") return One("BEACON_LIGHTS_SET", code == "215" ? 1u : 0u);
            if (code == "217" || code == "218" || code == "219") return One("NAV_LIGHTS_SET", code == "217" ? 0u : 1u);
            if (code == "225" || code == "226" || code == "227") return One("LANDING_LIGHTS_SET", code == "226" ? 1u : 0u);
            if (code == "223" || code == "224" || code == "228" || code == "229" || code == "230")
            {
                if (code == "223" || code == "224") runwayLight = code == "224";
                else noseLight = code != "228";
                return One("TAXI_LIGHTS_SET", runwayLight || noseLight ? 1u : 0u);
            }
            return new SimulatorCommand[0];
        }

        public IEnumerable<SimulatorCommand> UpdateAutomaticLights(SimulatorState state)
        {
            if (strobeAuto && state.Strobe != !state.OnGround)
                return One("STROBES_SET", state.OnGround ? 0u : 1u);
            return new SimulatorCommand[0];
        }

        public static IDictionary<string, string> HardwareOutputs(SimulatorState state)
        {
            return new Dictionary<string, string>
            {
                { "ap1", state.Autopilot ? "P," : "p," },
                { "ap2", state.NavigationHold ? "U," : "u," },
                { "alt-hold", state.AltitudeHold ? "a," : "q," },
                { "flc", state.FlightLevelChange ? "z," : "x," },
                { "heading-hold", state.HeadingHold ? "m," : "s," },
                { "speed-mode", state.FlightLevelChange ? "I," : "i," },
                { "speed", state.FlightLevelChange ? "S" + Math.Round(state.Speed).ToString(CultureInfo.InvariantCulture) + "," : "d," },
                { "heading-mode", "O," },
                { "heading", "H" + Math.Round(state.Heading).ToString(CultureInfo.InvariantCulture) + "," },
                { "altitude", "A" + Math.Round(state.Altitude).ToString(CultureInfo.InvariantCulture) + "," },
                { "vs-mode", "W," },
                { "vertical-speed", "V" + Math.Round(state.VerticalSpeed).ToString(CultureInfo.InvariantCulture) + "," }
            };
        }

        private static IEnumerable<SimulatorCommand> SetOrStep(InputFrame input, string setEvent, string stepEvent,
            uint step, uint minimum, uint maximum)
        {
            if (input.Value == null) return One(stepEvent, step);
            uint value;
            if (!UInt32.TryParse(input.Value, NumberStyles.None, CultureInfo.InvariantCulture, out value)
                || value < minimum || value > maximum)
                throw new ArgumentException("Invalid Caravan target for event " + input.Raw + ".");
            return One(setEvent, value);
        }

        private static IEnumerable<SimulatorCommand> One(string name, uint value)
        {
            return new[] { new SimulatorCommand(name, value) };
        }
    }
}
