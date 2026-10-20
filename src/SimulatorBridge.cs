using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Microsoft.FlightSimulator.SimConnect;

namespace MiniCockpitLab
{
    internal sealed class SimulatorBridge : IDisposable
    {
        public const int WindowMessage = 0x0402;
        private enum Definition { Caravan }
        private enum Request { State }
        private enum EventId { First }
        private enum Priority { Highest = 1 }
        private readonly Action<string> log;
        private readonly Dictionary<string, uint> eventIds = new Dictionary<string, uint>();
        private SimConnect client;
        private DateTime nextPoll;
        public bool Connected { get; private set; }
        public bool Connecting { get { return client != null && !Connected; } }
        public SimulatorState State { get; private set; }
        public string Status { get; private set; }
        public event Action StateChanged;

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi, Pack = 1)]
        private struct StateData
        {
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
            public string Aircraft;
            public double Autopilot, AltitudeHold, FlightLevelChange, Speed, Altitude;
            public double Strobe, Beacon, Navigation, Taxi, Landing, OnGround;
            public double Heading, VerticalSpeed, HeadingHold, VerticalHold, NavigationHold;
        }

        public SimulatorBridge(Action<string> writeLog)
        {
            log = writeLog;
            Status = "MSFS disconnected";
        }

        public bool Fresh
        {
            get { return Connected && State != null && DateTime.UtcNow - State.ReceivedUtc < TimeSpan.FromSeconds(3); }
        }

        public void Connect(IntPtr window)
        {
            if (client != null) throw new InvalidOperationException("MSFS is already connected or connecting.");
            try
            {
                client = new SimConnect("miniCOCKPIT", window, WindowMessage, null, 0);
                client.OnRecvOpen += OnOpen;
                client.OnRecvQuit += delegate { Disconnect("MSFS closed the connection."); };
                client.OnRecvException += delegate(SimConnect sender, SIMCONNECT_RECV_EXCEPTION data)
                {
                    Disconnect("SimConnect error: " + (SIMCONNECT_EXCEPTION)data.dwException
                        + " (send " + data.dwSendID + ", index " + data.dwIndex + ").");
                };
                client.OnRecvSimobjectDataBytype += OnState;
                Status = "Connecting to MSFS...";
                log(Status);
            }
            catch (COMException exception)
            {
                Disconnect("Cannot connect to MSFS: " + exception.Message);
                throw new InvalidOperationException(Status, exception);
            }
        }

        private void OnOpen(SimConnect sender, SIMCONNECT_RECV_OPEN data)
        {
            sender.AddToDataDefinition(Definition.Caravan, "TITLE", null, SIMCONNECT_DATATYPE.STRING256, 0, SimConnect.SIMCONNECT_UNUSED);
            string[] variables = { "AUTOPILOT MASTER", "AUTOPILOT ALTITUDE LOCK", "AUTOPILOT FLIGHT LEVEL CHANGE",
                "AUTOPILOT AIRSPEED HOLD VAR", "AUTOPILOT ALTITUDE LOCK VAR",
                "LIGHT STROBE", "LIGHT BEACON", "LIGHT NAV", "LIGHT TAXI", "LIGHT LANDING", "SIM ON GROUND",
                "AUTOPILOT HEADING LOCK DIR", "AUTOPILOT VERTICAL HOLD VAR", "AUTOPILOT HEADING LOCK", "AUTOPILOT VERTICAL HOLD",
                "AUTOPILOT NAV1 LOCK" };
            for (int i = 0; i < variables.Length; i++)
                sender.AddToDataDefinition(Definition.Caravan, variables[i],
                    i == 3 ? "knots" : i == 4 ? "feet" : i == 11 ? "degrees" : i == 12 ? "feet/minute" : "bool",
                    SIMCONNECT_DATATYPE.FLOAT64, 0, SimConnect.SIMCONNECT_UNUSED);
            sender.RegisterDataDefineStruct<StateData>(Definition.Caravan);
            Connected = true;
            Status = "MSFS connected / waiting for aircraft";
            nextPoll = DateTime.MinValue;
            log("MSFS connected: " + data.szApplicationName);
        }

        private void OnState(SimConnect sender, SIMCONNECT_RECV_SIMOBJECT_DATA_BYTYPE data)
        {
            if (data.dwRequestID != (uint)Request.State) return;
            var values = (StateData)data.dwData[0];
            State = new SimulatorState
            {
                Aircraft = values.Aircraft, ReceivedUtc = DateTime.UtcNow,
                Autopilot = values.Autopilot != 0, AltitudeHold = values.AltitudeHold != 0,
                FlightLevelChange = values.FlightLevelChange != 0, Speed = values.Speed, Altitude = values.Altitude,
                Strobe = values.Strobe != 0, Beacon = values.Beacon != 0, Navigation = values.Navigation != 0,
                Taxi = values.Taxi != 0, Landing = values.Landing != 0, OnGround = values.OnGround != 0,
                Heading = values.Heading, VerticalSpeed = values.VerticalSpeed,
                HeadingHold = values.HeadingHold != 0, VerticalHold = values.VerticalHold != 0,
                NavigationHold = values.NavigationHold != 0
            };
            Status = "MSFS connected / " + State.Aircraft
                + (State.IsCaravan ? "" : " / aircraft detection only");
            if (StateChanged != null) StateChanged();
        }

        public void Tick()
        {
            if (!Connected || DateTime.UtcNow < nextPoll) return;
            nextPoll = DateTime.UtcNow.AddMilliseconds(250);
            try { client.RequestDataOnSimObjectType(Request.State, Definition.Caravan, 0, SIMCONNECT_SIMOBJECT_TYPE.USER); }
            catch (COMException exception) { Disconnect("MSFS telemetry failed: " + exception.Message); }
        }

        public void ReceiveMessage()
        {
            if (client == null) return;
            try { client.ReceiveMessage(); }
            catch (COMException exception) { Disconnect("MSFS connection failed: " + exception.Message); }
        }

        public void Send(SimulatorCommand command)
        {
            if (!Fresh || !State.IsCaravan)
                throw new InvalidOperationException("No fresh Caravan telemetry. Reconnect MSFS before sending commands.");
            try
            {
                uint id;
                if (!eventIds.TryGetValue(command.Event, out id))
                {
                    id = (uint)eventIds.Count + 1;
                    client.MapClientEventToSimEvent((EventId)id, command.Event);
                    eventIds.Add(command.Event, id);
                }
                client.TransmitClientEvent(SimConnect.SIMCONNECT_OBJECT_ID_USER, (EventId)id, command.Value,
                    Priority.Highest, SIMCONNECT_EVENT_FLAG.GROUPID_IS_PRIORITY);
                log("MSFS TX " + command.Event + " = " + command.Value);
            }
            catch (COMException exception)
            {
                Disconnect("MSFS command failed: " + exception.Message);
                throw new InvalidOperationException(Status, exception);
            }
        }

        public void Disconnect(string reason)
        {
            SimConnect previous = client;
            client = null;
            Connected = false;
            State = null;
            eventIds.Clear();
            Status = reason;
            if (previous != null) previous.Dispose();
            log(reason);
            if (StateChanged != null) StateChanged();
        }

        public void Dispose()
        {
            Disconnect("MSFS disconnected");
        }
    }
}
