///Property of SteppIR
///Written by Emre Aksan
//Questions? eaksan1@gmail.com, +1-425-492-6769

/// Summary
/// Insert Summary here z z z z i shleep
/// Summary

///CREDIT: desktop link code is heavily reused from Stefan Wick's UWP with Desktop Extension tutorial
///https://stefanwick.com/2018/04/16/uwp-with-desktop-extension-part-3/



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
        private WaypointMission _currentMission;//any mission generated is stored here
        private const double radius = 9.144;//radius desired to fly in meters (UWB feild yellow circle is 9.144 meters)
        private const double degreesInMeterLat = .0000095;//at UWB, .0000095
        private const double degreesInMeterLon = .0000140;//at uwb, .0000140
        private const int pollRate = 150;//time in milliseconds to wait between data points
        private const double _cornerRadius = 1;//bezier curve radius
        private const double flightSpeed = 1;//speed to fly at
        private const int resolution = 8;//# of points in azimuth mission
        private const int resolutionElevation = 6;//# of points in elevation mission

        //Antenna Geometrics
        private DJI.WindowsSDK.Waypoint aboveAntenna;//Safe waypoint at min distace above antenna
        private double antennaElevation;//elevation of antenna
        private double antennaMinRadius;//minimum radius that can be flown around the antenna
        private double altitudeMaxGain = 0;//set by startLoggingElevation

        //globals for internal use
        private bool executing;//flag set to stop logging data
        private BoolMsg trueMsg;//for DJI SDK method calls
        private BoolMsg falseMsg;//ditto
        
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
        private async void setAntennaLocation(object sender, RoutedEventArgs e)//sets aboveAntenna
        {
            //result from getaircraftlocation
            ResultValue<LocationCoordinate2D?> place;
            ResultValue<DoubleMsg?> height;
            //Get current latitude, longitude, elevation
            place = await DJISDKManager.Instance.ComponentManager.GetFlightControllerHandler(0, 0).GetAircraftLocationAsync();
            height = await DJISDKManager.Instance.ComponentManager.GetFlightControllerHandler(0, 0).GetAltitudeAsync();
            //set antennaLocation
            aboveAntenna.location = place.value.Value;
            aboveAntenna.altitude = height.value.Value.value;
        }
        private async void setAntennaElevation(object sender, RoutedEventArgs e)//sets antennaElevation and minradius
        {
            //height result and place result
            ResultValue<DoubleMsg?> height;
            ResultValue<LocationCoordinate2D?> place;
            //get elevation and location
            height = await DJISDKManager.Instance.ComponentManager.GetFlightControllerHandler(0, 0).GetAltitudeAsync();
            place = await DJISDKManager.Instance.ComponentManager.GetFlightControllerHandler(0, 0).GetAircraftLocationAsync();
            //set antenna elevation
            antennaElevation = height.value.Value.value;
            //calculate distance sqrt(a2+b2)
            double a = latToMeters(place.value.Value.latitude - aboveAntenna.location.latitude);
            double b = lonToMeters(place.value.Value.longitude - aboveAntenna.location.longitude);
            antennaMinRadius = Math.Sqrt(Math.Pow(a,2) + Math.Pow(b, 2));
        }
        private void generateAzimuthMission(object sender, RoutedEventArgs e)//makes azimuth mission using a unit circle
        {
            LocationCoordinate2D[] locations = new LocationCoordinate2D[resolution + 1];
            int pointCount = 0;
            for (double i = 0; i <= 360; i += 360 / resolution)
            {
                //get the offet in (x-latitude,y-longitude) using sin and cos with a unit circle, then multiplying by desired radius
                locations[pointCount] = makeCoordinate(
                    aboveAntenna.location.latitude + toLat(Math.Sin(i) * radius),//lat
                    aboveAntenna.location.longitude + toLon(Math.Cos(i) * radius));//lon

                System.Diagnostics.Debug.WriteLine(0 + Math.Sin(i) * radius);//lat in meters
                System.Diagnostics.Debug.WriteLine(0 + Math.Cos(i) * radius);//lon in meters
                pointCount++;
            }
            //add locations to azimuthMission
            DJI.WindowsSDK.Waypoint curr = new DJI.WindowsSDK.Waypoint();
            for (int i = 0; i < resolution + 1; i++)
            {
                curr = makeWaypoint(locations[i], altitudeMaxGain);//elevation is set here, can be antennaElevation or altitudeMaxGain
                curr.cornerRadiusInMeters = _cornerRadius;
                curr.speed = flightSpeed;
                _currentMission.waypoints.Add(curr);
            }
        }
        private void generateElevationMission(object sender, RoutedEventArgs e)//makes elevation mission using a unit circle
        {
            DJI.WindowsSDK.Waypoint[] locations = new DJI.WindowsSDK.Waypoint[resolutionElevation];
            int pointCount = 0;
            for (double i = 0; i <= 180; i += (180 / (resolutionElevation - 1)))
            {
                //get the offet in (x-latitude,y-longitude) using sin and cos with a unit circle, then multiplying by desired radius
                locations[pointCount] = makeWaypoint(
                    makeCoordinate(
                        aboveAntenna.location.latitude + toLat(Math.Cos(i * Math.PI / 180) * radius),//lat
                        aboveAntenna.location.longitude),//lon
                        antennaElevation + (Math.Sin(i * Math.PI / 180) * radius));//elevation

                System.Diagnostics.Debug.WriteLine(0 + Math.Cos(i * Math.PI/180) * radius);//lat in meters
                System.Diagnostics.Debug.WriteLine(0);//lon in meters
                System.Diagnostics.Debug.WriteLine(0 + (Math.Sin(i * Math.PI / 180) * radius));//elevation
                pointCount++;
            }
            //add locations to azimuthMission
            for (int i = 0; i < resolutionElevation; i++)
            {
                locations[i].cornerRadiusInMeters = _cornerRadius;
                locations[i].speed = flightSpeed;
                _currentMission.waypoints.Add(locations[i]);
            }
        }
        private async void InitMission(object sender, RoutedEventArgs e)//initializes and configures _altitudeMission
        {
            _currentMission = new WaypointMission()
            {
                waypointCount = 0,
                maxFlightSpeed = 10,
                autoFlightSpeed = flightSpeed,
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
                .LoadMission(_currentMission);
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
        private void GetState(object sender, RoutedEventArgs e)//returns the drone state to debug console
        {
            //Send .GetCurrentState() result to debug
            System.Diagnostics.Debug.WriteLine(
                DJISDKManager.Instance.WaypointMissionManager.GetWaypointMissionHandler(0)
                .GetCurrentState());
        }
        private async void StopMission(object sender, RoutedEventArgs e)//Stops the drone mid-execution of mission
        {
            System.Diagnostics.Debug.WriteLine(
                await DJISDKManager.Instance.WaypointMissionManager.GetWaypointMissionHandler(0)
                .StopMission());
        }
        private async void startLogging(object sender, RoutedEventArgs e)//logs angle and magnitude for azimuth mission
        {
            System.Diagnostics.Debug.WriteLine("Started logging");
            // Create sample file; replace if exists.
            Windows.Storage.StorageFolder storageFolder =
                Windows.Storage.ApplicationData.Current.LocalFolder;
            Windows.Storage.StorageFile azimuthFile =
                await storageFolder.CreateFileAsync("azimuthPlot.csv",
                    Windows.Storage.CreationCollisionOption.GenerateUniqueName);

            string pair =  "";
            executing = true;
            while (executing)//keep logging till mission stops executing
            {
                pair += (await getAngle() + ",");
                pair += (await getMagnitude());
                await Windows.Storage.FileIO.AppendTextAsync(azimuthFile, pair);
                System.Diagnostics.Debug.WriteLine(pair);
                pair = "";
                ////wait for time set by polling rate
                await Task.Delay(pollRate);
            }
            ////while(DJISDKManager.Instance.WaypointMissionManager.GetWaypointMissionHandler(0)
            ////    .GetCurrentState().ToString() == "EXECUTING")//keep logging till mission stops executing
        }
        private async void startLoggingElevation(object sender, RoutedEventArgs e)//logs angle and magnitude for elevation mission
        {
            ResultValue<DoubleMsg?> height;

            System.Diagnostics.Debug.WriteLine("Started logging");
            // Create sample file; replace if exists.
            Windows.Storage.StorageFolder storageFolder =
                Windows.Storage.ApplicationData.Current.LocalFolder;
            Windows.Storage.StorageFile azimuthFile =
                await storageFolder.CreateFileAsync("elevationPlot.csv",
                    Windows.Storage.CreationCollisionOption.GenerateUniqueName);

            string pair = "";
            string mag;
            executing = true;
            while (executing)//keep logging till mission stops executing
            {
                //get magnitude
                mag = await getMagnitude();
                //Update altitude of max gain
                if (altitudeMaxGain < Convert.ToDouble(mag))
                {
                    height = await DJISDKManager.Instance.ComponentManager.GetFlightControllerHandler(0, 0).GetAltitudeAsync();
                    altitudeMaxGain = height.value.Value.value;
                }
                //add angle and magnitude to file, output to debug
                pair += (await getAngleElevation() + ",");
                pair += mag;
                await Windows.Storage.FileIO.AppendTextAsync(azimuthFile, pair);
                System.Diagnostics.Debug.WriteLine(pair);
                //clear pair
                pair = "";
                //wait for time set by polling rate
                await Task.Delay(pollRate);
            }
        }
        private void stopLogging(object sender, RoutedEventArgs e)//sets flag for logging functions to stop logging
        {
            executing = false;
            System.Diagnostics.Debug.WriteLine("Stopped logging");
        }
        private async void AutoTakeoff(object sender, RoutedEventArgs e)//takeoff drone
        {
            //write result of StartTakeOffAsync() to debug console
            System.Diagnostics.Debug.WriteLine(
                await DJISDKManager.Instance.ComponentManager.GetFlightControllerHandler(0, 0)
                .StartTakeoffAsync());
        }
        //=============================== helper functions ============================================
        private async void Instance_SDKRegistrationEvent(SDKRegistrationState state, SDKError resultCode)//registers SDK with DJI
        {
            if (resultCode == SDKError.NO_ERROR)
            {
                System.Diagnostics.Debug.WriteLine("Register app successfully.");

                //The product connection state will be updated when it changes here.
                DJISDKManager.Instance.ComponentManager.GetProductHandler(0).ProductTypeChanged += async delegate (object sender, ProductTypeMsg? value)
                {
                    await Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, async () =>
                    {
                        if (value != null && value?.value != ProductType.UNRECOGNIZED)
                        {
                            System.Diagnostics.Debug.WriteLine("The Aircraft is connected now.");
                            //You can load/display your pages according to the aircraft connection state here.
                        }
                        else
                        {
                            System.Diagnostics.Debug.WriteLine("The Aircraft is disconnected now.");
                            //You can hide your pages according to the aircraft connection state here, or show the connection tips to the users.
                        }
                    });
                };

                //If you want to get the latest product connection state manually, you can use the following code
                var productType = (await DJISDKManager.Instance.ComponentManager.GetProductHandler(0).GetProductTypeAsync()).value;
                if (productType != null && productType?.value != ProductType.UNRECOGNIZED)
                {
                    System.Diagnostics.Debug.WriteLine("The Aircraft is connected now.");
                    //You can load/display your pages according to the aircraft connection state here.
                }
            }
            else
            {
                System.Diagnostics.Debug.WriteLine("Register SDK failed, the error is: ");
                System.Diagnostics.Debug.WriteLine(resultCode.ToString());
            }
        }
        private DJI.WindowsSDK.Waypoint makeWaypoint(LocationCoordinate2D _location, double _height)//waypoint contructor
        {
            DJI.WindowsSDK.Waypoint waypoint = new DJI.WindowsSDK.Waypoint();
            waypoint.location = _location;
            waypoint.altitude = _height;
            waypoint.cornerRadiusInMeters = radius;
            //waypoint.speed = flightSpeed;
            return waypoint;
        }
        private DJI.WindowsSDK.Waypoint makeWaypoint(double _lat, double _lon, double _height)//waypoint constructor
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
        private LocationCoordinate2D makeCoordinate(double _lat, double _lon)//coordinate constructor
        {
            LocationCoordinate2D _location = new LocationCoordinate2D();
            _location.latitude = _lat;
            _location.longitude = _lon;
            return _location;
        }
        private async Task<double> getAngle()//used to calculate angle in Logging Azimuth
        {
            //waypoint and its data
            ResultValue<LocationCoordinate2D?> place;

            //Get current latitude, longitude, and altitude
            place = await DJISDKManager.Instance.ComponentManager.GetFlightControllerHandler(0, 0).GetAircraftLocationAsync();

            //get two sides of triangle
            double y = latToMeters(place.value.Value.latitude - aboveAntenna.location.latitude);
            double x = lonToMeters(place.value.Value.longitude - aboveAntenna.location.longitude);

            //Right triangle, solve for angle, convert from radians to degrees
            //return (180 / Math.PI) * Math.Atan(x / y);
            if (x > 0 && y > 0) {//Q1
                return (180 / Math.PI) * Math.Atan(y/x);
            }
            else if (x < 0 && y > 0) {//Q2
                return ((180 / Math.PI) * Math.Abs(Math.Atan(x / y))) + 90;
            }
            else if (x < 0 && y < 0) {//Q3
                return ((180 / Math.PI) * Math.Atan(y / x)) + 180;
            }
            else {//Q4
                return ((180 / Math.PI) * Math.Abs(Math.Atan(x / y))) + 270;
            }

        }
        private async Task<double> getAngleElevation()//used to calculate angle in logging elevation
        {
            //waypoint and its data
            ResultValue<LocationCoordinate2D?> place;
            ResultValue<DoubleMsg?> height;

            //Get current latitude, longitude, and altitude
            place = await DJISDKManager.Instance.ComponentManager.GetFlightControllerHandler(0, 0).GetAircraftLocationAsync();
            height = await DJISDKManager.Instance.ComponentManager.GetFlightControllerHandler(0, 0).GetAltitudeAsync();

            //get two sides of triangle
            double y = latToMeters(place.value.Value.latitude - aboveAntenna.location.latitude);
            double x = height.value.Value.value;

            //Right triangle, solve for angle, convert from radians to degrees
            //return (180 / Math.PI) * Math.Atan(x / y);
            if (x > 0 && y > 0)
            {//Q1
                return (180 / Math.PI) * Math.Atan(y / x);
            }
            else if (x < 0 && y > 0)
            {//Q2
                return ((180 / Math.PI) * Math.Abs(Math.Atan(x / y))) + 90;
            }
            else if (x < 0 && y < 0)
            {//Q3
                return ((180 / Math.PI) * Math.Atan(y / x)) + 180;
            }
            else
            {//Q4
                return ((180 / Math.PI) * Math.Abs(Math.Atan(x / y))) + 270;
            }
            //return 60;
        }
        private async Task<string> getMagnitude()//uses destop link to ask Fulltrust to get magnitude via VISA
        {
            //Ask for Magnitude key value pair from desktop service
            ValueSet request = new ValueSet();
            request.Add("Magnitude", "GIMME");
            //get response
            AppServiceResponse response = await App.Connection.SendMessageAsync(request);

            //output to debug and return Magnitude
            string Magnitude = response.Message["Magnitude"].ToString();
            //System.Diagnostics.Debug.WriteLine(Magnitude);
            return Magnitude;
            //return "5\n";
        }
        private double toLon(double meters)//conversion parameters must be updated for different geolocations
        {
            return meters * degreesInMeterLon;
        }
        private double lonToMeters(double lon)//conversion parameters must be updated for different geolocations
        {
            return lon * (1 / degreesInMeterLon);
        }
        private double toLat(double meters)//conversion parameters must be updated for different geolocations
        {
            return meters * degreesInMeterLat;
        }
        private double latToMeters(double lat)//conversion parameters must be updated for different geolocations
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
            _currentMission.waypoints.Add(_waypoint);
            System.Diagnostics.Debug.WriteLine("added a waypoint at (" + _waypoint.location.latitude
                + ", " + _waypoint.location.longitude + ")");
        }
        private async void oneMeterTestLat(object sender, RoutedEventArgs e)//reports distance in lat and lon from aboveAntenna
        {
            ////Add antenna location
            //_currentMission.waypoints.Add(makeWaypoint(aboveAntenna.location, antennaElevation + .5));
            ////Make waypoint 1 meter away
            //LocationCoordinate2D place = makeCoordinate(aboveAntenna.location.latitude + toLat(1), aboveAntenna.location.longitude);
            ////Add waypoint at place, height
            //_currentMission.waypoints.Add(makeWaypoint(place, antennaElevation + .5));

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
            _currentMission.waypoints.Add(aboveAntenna);
            //Make waypoint 1 meter away
            LocationCoordinate2D place = makeCoordinate(aboveAntenna.location.latitude + toLat(3), aboveAntenna.location.longitude + toLon(3));
            //Add waypoint at place, height
            _currentMission.waypoints.Add(makeWaypoint(place, aboveAntenna.altitude));
        }
        private async void getLocation(object sender, RoutedEventArgs e)//prints location to debug terminal
        {
            DJI.WindowsSDK.ResultValue<LocationCoordinate2D?> place;
            place = await DJISDKManager.Instance.ComponentManager.GetFlightControllerHandler(0, 0).GetAircraftLocationAsync();
            System.Diagnostics.Debug.WriteLine(place.value.Value.ToString());
        }
        private void testlat(object sender, RoutedEventArgs e)//flys 9.144 meters north and back 2 times
        {
            _currentMission.waypoints.Add(makeWaypoint(makeCoordinate(aboveAntenna.location.latitude, aboveAntenna.location.longitude), antennaElevation));
            _currentMission.waypoints.Add(makeWaypoint(makeCoordinate(aboveAntenna.location.latitude + toLat(9.144), aboveAntenna.location.longitude), antennaElevation));
            _currentMission.waypoints.Add(makeWaypoint(makeCoordinate(aboveAntenna.location.latitude, aboveAntenna.location.longitude), antennaElevation));
            _currentMission.waypoints.Add(makeWaypoint(makeCoordinate(aboveAntenna.location.latitude + toLat(9.144), aboveAntenna.location.longitude), antennaElevation));
        }
        private void testlon(object sender, RoutedEventArgs e)//flys 9.144 meters east and back 2 times
        {
            _currentMission.waypoints.Add(makeWaypoint(makeCoordinate(aboveAntenna.location.latitude, aboveAntenna.location.longitude), antennaElevation));
            _currentMission.waypoints.Add(makeWaypoint(makeCoordinate(aboveAntenna.location.latitude, aboveAntenna.location.longitude + toLon(9.144)), antennaElevation));
            _currentMission.waypoints.Add(makeWaypoint(makeCoordinate(aboveAntenna.location.latitude, aboveAntenna.location.longitude), antennaElevation));
            _currentMission.waypoints.Add(makeWaypoint(makeCoordinate(aboveAntenna.location.latitude, aboveAntenna.location.longitude + toLon(9.144)), antennaElevation));
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
