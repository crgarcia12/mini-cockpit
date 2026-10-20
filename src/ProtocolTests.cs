using System;
using System.Collections.Generic;
using System.Linq;
using MiniCockpitLab;

internal static class ProtocolTests
{
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }

    private static void Main()
    {
        var parser = new TokenParser();
        var tokens = new List<string>();
        parser.Feed("90", tokens.Add);
        Check(tokens.Count == 0, "Partial token must wait.");
        parser.Feed("1;956;9", tokens.Add);
        parser.Feed("58;\r\n;13;9999;custom:value;", tokens.Add);
        Check(String.Join(",", tokens) == "901,956,958,13,9999,custom:value", "Preserve split, combined, and unknown tokens.");
        parser.Feed("old", tokens.Add);
        parser.Reset();
        parser.Feed("new;", tokens.Add);
        Check(tokens[tokens.Count - 1] == "new", "Reconnect must discard partial old data.");
        Check(Baseline.Describe("999").StartsWith("Unknown"), "Do not invent meanings for undocumented codes.");
        var frame = InputFrame.Parse("17,12300");
        Check(frame.Code == "17" && frame.Value == "12300" && frame.Raw == "17,12300", "Separate event code and value without losing raw data.");
        Check(InputFrame.Parse("17,12500").Code == frame.Code, "Changing values must not create new input identities.");
        Check(InputFrame.Parse("901").Code == "901" && InputFrame.Parse("901").Value == null, "Do not strip leading digits.");
        Check(InputFrame.Parse("custom,value").Code == "custom,value", "Preserve non-numeric opaque tokens.");
        Check(InputFrame.Parse("21,-700").Value == "-700", "Preserve signed values.");
        var mappings = new Dictionary<string, string> { { "17", "Altitude clockwise" }, { "17,12300", "Specific position" } };
        Check(frame.FindMapping(mappings) == "Specific position", "Exact value mappings must override generic mappings.");
        Check(InputFrame.Parse("17,12500").FindMapping(mappings) == "Altitude clockwise", "Generic mappings must work for new values.");
        mappings["17,12300"] = "";
        Check(frame.FindMapping(mappings) == "Altitude clockwise", "Legacy empty labels must not mask useful mappings.");
        Check(InputFrame.Parse("999").FindMapping(mappings) == null, "Unknown inputs must remain unmapped.");
        var panels = new PanelConnections();
        Check(!panels.Fcu && !panels.Efis && !panels.Overhead, "Panels must start unconfirmed.");
        panels.Observe("17");
        Check(!panels.Fcu, "A control event must not invent a panel connection.");
        panels.Observe("901");
        panels.Observe("956");
        panels.Observe("958");
        Check(panels.Fcu && panels.Efis && panels.Overhead, "Connection reports must light the corresponding panels.");
        panels.Observe("957");
        Check(panels.Fcu && !panels.Efis && panels.Overhead, "EFIS disconnect must not change the other panels.");
        panels.Observe("959");
        Check(!panels.Overhead, "Overhead disconnect must clear its light.");
        panels.Observe("956");
        panels.Observe("958");
        panels.Reset();
        Check(!panels.Fcu && !panels.Efis && !panels.Overhead, "Disconnect/reconnect must clear stale confirmations.");
        TestCaravan();
        bool rejected = false;
        try { parser.Feed(new string('x', 4097), tokens.Add); }
        catch (FormatException) { rejected = true; }
        Check(rejected, "Unframed data must surface an explicit error.");
        Console.WriteLine("Protocol tests passed: framing, code/value separation, stable identities, and exact/generic mapping resolution.");
    }

    private static void TestCaravan()
    {
        Check(AircraftProfiles.Detect("C208B Cargo") == AircraftProfiles.Caravan
            && AircraftProfiles.Detect("cessna grand caravan ex") == AircraftProfiles.Caravan
            && AircraftProfiles.Detect("FlyByWire_A32NX") == "Airbus A320neo"
            && AircraftProfiles.Detect("Airbus A321 Neo") == "Airbus A321neo"
            && AircraftProfiles.Detect("Headwind A330-900") == "Airbus A330-900"
            && AircraftProfiles.Detect("A339X") == "Airbus A330-900",
            "Aircraft detection must recognize simulator titles and supported add-on variants.");
        Check(AircraftProfiles.Detect(null) == null && AircraftProfiles.Detect("Cessna 172") == null
            && AircraftProfiles.Detect("Unknown aircraft") == null && AircraftProfiles.Detect("A330-200") == null,
            "Missing or unsupported titles must never select a different aircraft profile.");
        var profile = new CaravanProfile();
        var state = new SimulatorState { Aircraft = "Cessna 208B Grand Caravan EX", FlightLevelChange = true, OnGround = true };
        Action<string, string, uint> expect = (input, name, value) =>
        {
            SimulatorCommand command = profile.Handle(InputFrame.Parse(input), state).Single();
            Check(command.Event == name && command.Value == value, "Incorrect Caravan mapping for " + input);
        };
        expect("50", "AP_MASTER", 0);
        expect("51", "AP_NAV1_HOLD", 0);
        expect("1", "AP_PANEL_HEADING_ON", 0);
        expect("2", "AP_PANEL_HEADING_ON", 0);
        expect("15", "AP_ALT_HOLD_ON", 0);
        expect("16", "AP_ALT_HOLD_ON", 0);
        expect("11", "FLIGHT_LEVEL_CHANGE_ON", 0);
        expect("12", "FLIGHT_LEVEL_CHANGE_ON", 0);
        expect("3,234", "HEADING_BUG_SET", 234);
        expect("4", "HEADING_BUG_DEC", 0);
        expect("13", "AP_SPD_VAR_INC", 1);
        expect("14", "AP_SPD_VAR_DEC", 1);
        expect("13,117", "AP_SPD_VAR_SET", 117);
        expect("14,0", "AP_SPD_VAR_SET", 0);
        expect("17,12300", "AP_ALT_VAR_INC", 100);
        expect("18", "AP_ALT_VAR_DEC", 100);
        state.Altitude = 11300;
        expect("17,12300", "AP_ALT_VAR_INC", 1000);
        state.Altitude = 0;
        expect("21,900", "AP_VS_VAR_SET_ENGLISH", 900);
        expect("22,-700", "AP_VS_VAR_SET_ENGLISH", unchecked((uint)-700));
        expect("213", "STROBES_SET", 1);
        expect("212", "STROBES_SET", 0);
        expect("215", "BEACON_LIGHTS_SET", 1);
        expect("216", "BEACON_LIGHTS_SET", 0);
        expect("218", "NAV_LIGHTS_SET", 1);
        expect("219", "NAV_LIGHTS_SET", 1);
        expect("217", "NAV_LIGHTS_SET", 0);
        expect("226", "LANDING_LIGHTS_SET", 1);
        expect("225", "LANDING_LIGHTS_SET", 0);
        expect("227", "LANDING_LIGHTS_SET", 0);
        expect("224", "TAXI_LIGHTS_SET", 1);
        expect("230", "TAXI_LIGHTS_SET", 1);
        expect("223", "TAXI_LIGHTS_SET", 1);
        expect("228", "TAXI_LIGHTS_SET", 0);
        expect("214", "STROBES_SET", 0);
        state.OnGround = false;
        Check(profile.UpdateAutomaticLights(state).Single().Value == 1, "Strobe AUTO must turn on after leaving the ground.");
        profile.Reset();
        Check(!profile.UpdateAutomaticLights(state).Any(), "Disconnect must reset strobe AUTO ownership.");
        Check(!profile.Handle(InputFrame.Parse("577"), state).Any(), "APU release must never become an autopilot command.");
        bool rejected = false;
        try { profile.Handle(InputFrame.Parse("13,9999"), state).ToArray(); }
        catch (ArgumentException) { rejected = true; }
        Check(rejected, "Out-of-range speed must not reach MSFS.");
        state.FlightLevelChange = false;
        rejected = false;
        try { profile.Handle(InputFrame.Parse("13"), state).ToArray(); }
        catch (InvalidOperationException) { rejected = true; }
        Check(rejected, "Speed inputs must not operate outside FLC.");
        state.Autopilot = true;
        state.AltitudeHold = true;
        state.Altitude = 4500;
        state.Speed = 117;
        state.Heading = 234;
        state.VerticalSpeed = -700;
        var outputs = CaravanProfile.HardwareOutputs(state);
        Check(outputs["ap1"] == "P," && outputs["ap2"] == "u," && outputs["alt-hold"] == "a,"
            && outputs["speed-mode"] == "i," && outputs["speed"] == "d," && outputs["altitude"] == "A4500,"
            && outputs["heading"] == "H234," && outputs["vertical-speed"] == "V-700,",
            "Hardware lights and displays must use simulator readbacks, not optimistic button states.");
        state.FlightLevelChange = true;
        state.Navigation = true;
        state.HeadingHold = true;
        outputs = CaravanProfile.HardwareOutputs(state);
        Check(outputs["speed-mode"] == "I," && outputs["speed"] == "S117," && outputs["heading-hold"] == "m," && outputs["ap2"] == "u,",
            "FLC must restore selected speed without lighting AP2; exterior NAV lights must not light AP2 either.");
        state.FlightLevelChange = false;
        state.Navigation = false;
        state.NavigationHold = true;
        outputs = CaravanProfile.HardwareOutputs(state);
        Check(outputs["ap2"] == "U," && outputs["flc"] == "x," && outputs["speed"] == "d,",
            "AP2 must follow autopilot NAV independently of FLC and exterior navigation lights.");
        state.AltitudeHold = true;
        expect("1", "AP_PANEL_HEADING_ON", 0);
        expect("2", "AP_PANEL_HEADING_ON", 0);
        expect("15", "AP_ALT_HOLD_ON", 0);
        expect("16", "AP_ALT_HOLD_ON", 0);
        expect("11", "FLIGHT_LEVEL_CHANGE_ON", 0);
        expect("12", "FLIGHT_LEVEL_CHANGE_ON", 0);
        state.Aircraft = "Airbus A320neo";
        rejected = false;
        try { profile.Handle(InputFrame.Parse("50"), state).ToArray(); }
        catch (InvalidOperationException) { rejected = true; }
        Check(rejected, "Caravan commands must never operate a different aircraft.");
        Console.WriteLine("Caravan profile tests passed: AP, ALT, FLC, target values, exterior lights, readbacks, and aircraft guards.");
    }
}
