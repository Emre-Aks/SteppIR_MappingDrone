///Property of SteppIR
///Written by Emre Aksan eaksan1@gmail.com
///CREDIT: desktop link code is heavily reused from Stefan Wick's UWP with Desktop Extension tutorial
///https://stefanwick.com/2018/04/16/uwp-with-desktop-extension-part-3/

/// Summary
/// Insert Summary here z z z z i shleep
/// Summary
/// 

///TODO
///add/update function summareis
///match to 80 character max line length
///TODO

using DJI.WindowsSDK;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading;
using System.Threading.Tasks;
using Windows.ApplicationModel;
using Windows.ApplicationModel.AppService;
using Windows.Foundation;
using Windows.Foundation.Collections;
using Windows.Foundation.Metadata;
using Windows.UI.Core;
using Windows.UI.Popups;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml.Data;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Navigation;

namespace UWP
{
    public sealed partial class MainPage : Page
    {
        //Mission Data
        private WaypointMission _azimuthMission;//mission to fly around antenna
        private WaypointMission _altitudeMission;//mission to fly above antenna
        private double radius = 9.144;//radius desired to fly in meters
        private double _cornerRadius = 1;

        //Antenna Geometrics
        private DJI.WindowsSDK.Waypoint aboveAntenna;//Safe waypoint at min distace above antenna
        private double antennaElevation;//elevation of antenna
        private double antennaMinRadius;//minimum radius that can be flown around the antenna

        //For internal use
        bool executing;
        BoolMsg trueMsg;
        BoolMsg falseMsg;
        double degreesInMeterLat = .00001;
        double degreesInMeterLon =.00001;
        public MainPage()
        {
            this.InitializeComponent();

            //Register Class instance using code from DJI Dev page
            DJISDKManager.Instance.SDKRegistrationStateChanged += Instance_SDKRegistrationEvent;
            DJISDKManager.Instance.RegisterApp("85e13f243fee3ecf88806509");

            trueMsg.value = true;
            falseMsg.value = false;
        }

        //=================================== UI Buttons =============================================================
        private async void setAntennaLocation(object sender, RoutedEventArgs e)//for logging antenna location
        {
            //result from getaircraftlocation
            ResultValue<LocationCoordinate2D?> place;
            ResultValue<DoubleMsg?> height;
            //Get current latitude, longitude
            place = await DJISDKManager.Instance.ComponentManager.GetFlightControllerHandler(0, 0).GetAircraftLocationAsync();
            height = await DJISDKManager.Instance.ComponentManager.GetFlightControllerHandler(0, 0).GetAltitudeAsync();
            //set antennaLocation
            aboveAntenna.location = place.value.Value;
            aboveAntenna.altitude = height.value.Value.value;
        }
        private async void setAntennaElevation(object sender, RoutedEventArgs e)//for logging antenna height
        {
            //height result and place result
            ResultValue<DoubleMsg?> height;
            ResultValue<LocationCoordinate2D?> place;
            //get elevation and location
            height = await DJISDKManager.Instance.ComponentManager.GetFlightControllerHandler(0, 0).GetAltitudeAsync();
            place = await DJISDKManager.Instance.ComponentManager.GetFlightControllerHandler(0, 0).GetAircraftLocationAsync();
            //set antenna elevation
            antennaElevation = height.value.Value.value;
        }
        private void generateAzimuthMission(object sender, RoutedEventArgs e)//needs testing, #3
        {
            int resolution = 8;
            LocationCoordinate2D[] locations = new LocationCoordinate2D[resolution + 1];
            int pointCount = 0;
            for (double i = 0; i <= 360; i += 360 / resolution)
            {
                //get the offet in (x-latitude,y-longitude) using sin and cos with a unit circle, then multiplying by desired radius
                locations[pointCount] = makeCoordinate(aboveAntenna.location.latitude + toLat(Math.Sin(i) * radius),
                    aboveAntenna.location.longitude + toLon(Math.Cos(i) * radius));
                pointCount++;
            }
            //add locations to azimuthMission
            DJI.WindowsSDK.Waypoint curr = new DJI.WindowsSDK.Waypoint();
            for (int i = 0; i < resolution + 1; i++)
            {
                curr = makeWaypoint(locations[i], antennaElevation);
                curr.cornerRadiusInMeters = _cornerRadius;
                _azimuthMission.waypoints.Add(curr);
            }
        }
        private async void InitMission(object sender, RoutedEventArgs e)//resets and configures _altitudeMission
        {
            _azimuthMission = new WaypointMission()
            {
                waypointCount = 0,
                maxFlightSpeed = 15,
                autoFlightSpeed = 10,
                finishedAction = WaypointMissionFinishedAction.NO_ACTION,
                headingMode = WaypointMissionHeadingMode.TOWARD_POINT_OF_INTEREST,
                flightPathMode = WaypointMissionFlightPathMode.CURVED,
                gotoFirstWaypointMode = WaypointMissionGotoFirstWaypointMode.SAFELY,
                exitMissionOnRCSignalLostEnabled = false,
                pointOfInterest = new LocationCoordinate2D()
                {
                    latitude = aboveAntenna.location.latitude,
                    longitude = aboveAntenna.location.longitude
                },
                gimbalPitchRotationEnabled = true,
                repeatTimes = 0,
                missionID = 0,
                waypoints = new List<Waypoint>()
            };
            //for error reporting
            DJI.WindowsSDK.SDKError resultcode;
            //set ground station mode to true, enable object avoidance, and  vision assisted flight
            resultcode = await DJISDKManager.Instance.ComponentManager.GetFlightControllerHandler(0, 0)
                .SetGroundStationModeEnabledAsync(trueMsg);
            System.Diagnostics.Debug.WriteLine(resultcode.ToString());
            resultcode = await DJISDKManager.Instance.ComponentManager.GetFlightAssistantHandler(0, 0)
                .SetObstacleAvoidanceEnabledAsync(falseMsg);
            System.Diagnostics.Debug.WriteLine(resultcode.ToString());
            resultcode = await DJISDKManager.Instance.ComponentManager.GetFlightAssistantHandler(0, 0)
                .SetVisionAssistedPositioningEnabledAsync(trueMsg);
            System.Diagnostics.Debug.WriteLine(resultcode.ToString());
        }
        private async void UploadMission(object sender, RoutedEventArgs e)//uploads the mission to the drone
        {
            //for error reporting
            DJI.WindowsSDK.SDKError resultcode;
            //give mission to class, upload to drone, start mission
            resultcode = DJISDKManager.Instance.WaypointMissionManager.GetWaypointMissionHandler(0)
                .LoadMission(_azimuthMission);
            System.Diagnostics.Debug.WriteLine(resultcode.ToString());
            resultcode = await DJISDKManager.Instance.WaypointMissionManager.GetWaypointMissionHandler(0)
                .UploadMission();
            System.Diagnostics.Debug.WriteLine(resultcode.ToString());
        }
        private async void ExecuteMission(object sender, RoutedEventArgs e)//executes the mission currently on the drone
        {
            DJI.WindowsSDK.SDKError resultcode;
            resultcode = await DJISDKManager.Instance.WaypointMissionManager.GetWaypointMissionHandler(0)
                .StartMission();
            System.Diagnostics.Debug.WriteLine(resultcode.ToString());
        }
        private void GetState(object sender, RoutedEventArgs e)
        {
            //Send .GetCurrentState() result to debug
            System.Diagnostics.Debug.WriteLine(
                DJISDKManager.Instance.WaypointMissionManager.GetWaypointMissionHandler(0)
                .GetCurrentState());
        }
        private async void StopMission(object sender, RoutedEventArgs e)
        {
            System.Diagnostics.Debug.WriteLine(
                await DJISDKManager.Instance.WaypointMissionManager.GetWaypointMissionHandler(0)
                .StopMission());
        }
        private async void startLogging(object sender, RoutedEventArgs e)
        {
            Windows.Storage.StorageFolder storageFolder = Windows.Storage.ApplicationData.Current.LocalFolder;
            Windows.Storage.StorageFile azimuthFile = await storageFolder.GetFileAsync("AzimuthData");

            executing = true;
            string pair =  "";
            while(executing)//this will be set false by the stopLogging button
            {
                pair += ("(" + await getAngle() + ",");//does these await work as intended?
                pair += (await getMagnitude() + ") ");
                await Windows.Storage.FileIO.WriteTextAsync(azimuthFile, pair);
                pair = "";
                //wait for time set by polling rate
            }
        }
        private void stopLogging(object sender, RoutedEventArgs e)
        {
            executing = false;
        }
        private async void AutoTakeoff(object sender, RoutedEventArgs e) //takeoff drone
        {
            //write result of StartTakeOffAsync() to debug console
            System.Diagnostics.Debug.WriteLine(
                await DJISDKManager.Instance.ComponentManager.GetFlightControllerHandler(0, 0)
                .StartTakeoffAsync());
        }
        //=============================== helper functions ============================================
        private DJI.WindowsSDK.Waypoint makeWaypoint(LocationCoordinate2D _location, double _height)
        {
            DJI.WindowsSDK.Waypoint waypoint = new DJI.WindowsSDK.Waypoint();
            waypoint.location = _location;
            waypoint.altitude = _height;
            waypoint.cornerRadiusInMeters = radius;
            return waypoint;
        }
        private DJI.WindowsSDK.Waypoint makeWaypoint(double _lat, double _lon, double _height)
        {
            LocationCoordinate2D _location = new LocationCoordinate2D();
            _location.latitude = _lat;
            _location.longitude = _lon;
            DJI.WindowsSDK.Waypoint waypoint = new DJI.WindowsSDK.Waypoint();
            waypoint.location = _location;
            waypoint.altitude = _height;
            waypoint.cornerRadiusInMeters = radius;
            return waypoint;
        }
        private LocationCoordinate2D makeCoordinate(double _lat, double _lon)
        {
            LocationCoordinate2D _location = new LocationCoordinate2D();
            _location.latitude = _lat;
            _location.longitude = _lon;
            return _location;
        }
        private async Task<double> getAngle()
        {
            //waypoint and its data
            ResultValue<LocationCoordinate2D?> place;

            //Get current latitude, longitude, and altitude
            place = await DJISDKManager.Instance.ComponentManager.GetFlightControllerHandler(0, 0).GetAircraftLocationAsync();

            //get two sides of triangle
            double x = latToMeters(place.value.Value.latitude - aboveAntenna.location.latitude);
            double y = lonToMeters(place.value.Value.longitude - aboveAntenna.location.longitude);

            //Right triangle, solve for angle
            return Math.Asin(y / x);
        }
        private async Task<string> getMagnitude()
        {
            //Ask for Magnitude key value pair from desktop service
            ValueSet request = new ValueSet();
            request.Add("Magnitude", "GIMME");
            //get response
            AppServiceResponse response = await App.Connection.SendMessageAsync(request);

            //output to debug and return Magnitude
            string Magnitude = response.Message["Magnitude"].ToString();
            System.Diagnostics.Debug.WriteLine(Magnitude);
            return Magnitude;
        }
        private double toLon(double meters)
        {
            return meters * degreesInMeterLon;
        }
        private double lonToMeters(double lon)
        {
            return lon * (1 / degreesInMeterLon);
        }
        private double toLat(double meters)
        {
            return meters * degreesInMeterLat;
        }
        private double latToMeters(double lat)
        {
            return lat * (1 / degreesInMeterLat);
        }
        //===========================testing functions=====================================================
        private async void AddWaypointAtCurrentLocation(object sender, RoutedEventArgs e)//adds a waypoint to _altitudeMission at current location
        {
            //waypoint and its data
            ResultValue<LocationCoordinate2D?> place;
            ResultValue<DoubleMsg?> height;
            //Get current latitude, longitude, and altitude
            place = await DJISDKManager.Instance.ComponentManager.GetFlightControllerHandler(0, 0).GetAircraftLocationAsync();
            height = await DJISDKManager.Instance.ComponentManager.GetFlightControllerHandler(0, 0).GetAltitudeAsync();
            //Add waypoint
            DJI.WindowsSDK.Waypoint _waypoint = new DJI.WindowsSDK.Waypoint();
            _waypoint.location = place.value.Value;
            _waypoint.altitude = height.value.Value.value;
            _waypoint.cornerRadiusInMeters = 3;
            _azimuthMission.waypoints.Add(_waypoint);
            System.Diagnostics.Debug.WriteLine("added a waypoint at (" + _waypoint.location.latitude
                + ", " + _waypoint.location.longitude + ")");
        }
        private async void oneMeterTestLat(object sender, RoutedEventArgs e)//reports distance in lat and lon from aboveAntenna
        {
            ////Add antenna location
            //_azimuthMission.waypoints.Add(makeWaypoint(aboveAntenna.location, antennaElevation + .5));
            ////Make waypoint 1 meter away
            //LocationCoordinate2D place = makeCoordinate(aboveAntenna.location.latitude + toLat(1), aboveAntenna.location.longitude);
            ////Add waypoint at place, height
            //_azimuthMission.waypoints.Add(makeWaypoint(place, antennaElevation + .5));

            //waypoint and its data
            ResultValue<LocationCoordinate2D?> place;
            ResultValue<DoubleMsg?> height;
            //Get current latitude, longitude, and altitude
            place = await DJISDKManager.Instance.ComponentManager.GetFlightControllerHandler(0, 0).GetAircraftLocationAsync();
            height = await DJISDKManager.Instance.ComponentManager.GetFlightControllerHandler(0, 0).GetAltitudeAsync();
            //Add waypoint
            DJI.WindowsSDK.Waypoint _waypoint = new DJI.WindowsSDK.Waypoint();
            _waypoint.location = place.value.Value;
            _waypoint.altitude = height.value.Value.value;

            double differenceLat = aboveAntenna.location.latitude - _waypoint.location.latitude;
            double differenceLon = aboveAntenna.location.longitude - _waypoint.location.longitude;

            System.Diagnostics.Debug.WriteLine("Difference in lat is " + differenceLat);
            System.Diagnostics.Debug.WriteLine("Difference in lon is " + differenceLon);
        }
        private void oneMeterTestLon(object sender, RoutedEventArgs e)//flys to aboveAntenna then 1 meter sideways and 1 meter foreward
        {
            //Add antenna location
            _azimuthMission.waypoints.Add(aboveAntenna);
            //Make waypoint 1 meter away
            LocationCoordinate2D place = makeCoordinate(aboveAntenna.location.latitude + toLat(3), aboveAntenna.location.longitude + toLon(3));
            //Add waypoint at place, height
            _azimuthMission.waypoints.Add(makeWaypoint(place, aboveAntenna.altitude));
        }
        private async void getLocation(object sender, RoutedEventArgs e)//prints location to debug terminal
        {
            DJI.WindowsSDK.ResultValue<LocationCoordinate2D?> place;
            place = await DJISDKManager.Instance.ComponentManager.GetFlightControllerHandler(0, 0).GetAircraftLocationAsync();
            System.Diagnostics.Debug.WriteLine(place.value.Value.ToString());
        }

        //=========================== unused utility functions =============================================
        private async void SetHome(object sender, RoutedEventArgs e)
        {
            //send result of .SetHomeLocationUsingAircraftCurrentLocationAsync() to debug console
            System.Diagnostics.Debug.WriteLine(
                await DJISDKManager.Instance.ComponentManager.GetFlightControllerHandler(0, 0)
                .SetHomeLocationUsingAircraftCurrentLocationAsync());
        }
        private async void GoHome(object sender, RoutedEventArgs e)
        {
            //Send result of .StartGoHomeAsync() to debug
            System.Diagnostics.Debug.WriteLine(
                await DJISDKManager.Instance.ComponentManager.GetFlightControllerHandler(0, 0)
                .StartGoHomeAsync());
        }
        private async void CalibrateCompass(object sender, RoutedEventArgs e)
        {
            System.Diagnostics.Debug.WriteLine(
                await DJISDKManager.Instance.ComponentManager.GetFlightControllerHandler(0, 0)
                .StartCompasCalibrationAsync());
        }
        private async void CalibrateIMU(object sender, RoutedEventArgs e)
        {
            System.Diagnostics.Debug.WriteLine(
                await DJISDKManager.Instance.ComponentManager.GetFlightControllerHandler(0, 0)
                .StartIMUCalibrationAsync());
        }

        //========================= Desktop Link code, credit Stefan Wick ================================
        protected async override void OnNavigatedTo(NavigationEventArgs e)//kick off the desktop process and listen to app service connection events
        {
            base.OnNavigatedTo(e);

            if (ApiInformation.IsApiContractPresent("Windows.ApplicationModel.FullTrustAppContract", 1, 0))
            {
                App.AppServiceConnected += MainPage_AppServiceConnected;
                App.AppServiceDisconnected += MainPage_AppServiceDisconnected;
                await FullTrustProcessLauncher.LaunchFullTrustProcessForCurrentAppAsync();
            }
        }
        private async void MainPage_AppServiceConnected(object sender, AppServiceTriggerDetails e)// When the desktop process is connected, get ready to receive requests
        {
            await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
            {
                // enable UI to access  the connection
                //btnRegKey.IsEnabled = true;
            });
        }
        private async void MainPage_AppServiceDisconnected(object sender, EventArgs e)// When the desktop process is disconnected, reconnect if needed
        {
            await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, ()=>
            {
                // disable UI to access the connection
                //btnRegKey.IsEnabled = false;

                // ask user if they want to reconnect
                Reconnect();
            });
        }
        private async void Reconnect()// Ask user if they want to reconnect to the desktop process
        {
            if (App.IsForeground)
            {
                MessageDialog dlg = new MessageDialog("Connection to desktop process lost. Reconnect?");
                UICommand yesCommand = new UICommand("Yes", async (r) =>
                {
                    await FullTrustProcessLauncher.LaunchFullTrustProcessForCurrentAppAsync();
                });
                dlg.Commands.Add(yesCommand);
                UICommand noCommand = new UICommand("No", (r) => { });
                dlg.Commands.Add(noCommand);
                await dlg.ShowAsync();
            }
        }
    }
}
