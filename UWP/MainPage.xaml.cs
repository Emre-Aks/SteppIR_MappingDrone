///Property of SteppIR
///Written by Emre Aksan
//Questions? eaksan1@gmail.com, +1-425-492-6769

//IMPORTANT!
///You need an internet coneection for the program to function properly. Programs written with the 
///DJI SDK need to authenticate themselves with DJI every so often.

/// This program is very much unfinished, it's a proof of concept for the idea of mapping an
/// antennna's radiation pattern using a drone. The UI isn't meant for real users, but the programmer.

// Summary
/// This program flies a payload coil shortened dipole around an antenna in free space using a DJI
/// Phantom 4 Pro v2, and creates a CSV of the antenna under test's (AUT) radiation pattern in 
/// azimuth and elevation.
/// Data is gathered from a spectrum analyzer and the angle that data was taken at is calculated from
/// the drone's current position and the AUT's marked position.

///CREDIT: desktop link code is heavily reused from Stefan Wick's UWP with Desktop Extension tutorial
///https://stefanwick.com/2018/04/16/uwp-with-desktop-extension-part-3/

// General Architecture Explanation
/// There are two programs that run to make this software function. These are the two projects
/// entitled 'UWP', and the project entitled 'FullTrust'. UWP contains all functionality, other
/// than communication with the spectrum analyzer, which is handled in the FullTrust project. The
/// Solution contains 3 projects; the third project 'Package' packages the two together. 
/// Information is sent over desktop link from 'Fulltrust' to 'UWP'. For more details visit stefan
/// wicks tutorial linked above.

///Good luck steppIR 3!


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
        private const double radius = 9.144;//radius desired to fly in meters
                                            //(UWB feild yellow circle is 9.144 meters)
        private const double degreesInMeterLat = .0000095;//at UWB, .0000095
        private const double degreesInMeterLon = .0000140;//at uwb, .0000140
        private const int pollRate = 200;//time in milliseconds to wait between data points
        private const double _cornerRadius = 1;//bezier curve radius
        private const double flightSpeedAzimuth = .44;//speed to fly at
        private const double flightSpeedElevation = .22;//speed to fly at
        private const int resolutionAzimuth = 8;//# of points in azimuth mission
        private const int resolutionElevation = 6;//# of points in elevation mission

        //Antenna Geometrics
        private DJI.WindowsSDK.Waypoint aboveAntenna;//Safe waypoint at min distace above antenna
        private double antennaElevation;//elevation of antenna
        private double antennaMinRadius;//minimum radius that can be flown around the antenna
        private double maxGain = 10;//set by startLoggingElevation
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

        //============================== Buttons in UI Call these =================================
        /// <summary>
        /// After flying above the AUT, this function is called to mark its latitude and longitude
        /// </summary>
        private async void setAntennaLocation(object sender, RoutedEventArgs e)
        {
            //result from getaircraftlocation
            ResultValue<LocationCoordinate2D?> place;
            ResultValue<DoubleMsg?> height;
            //Get current latitude, longitude, elevation
            place = await DJISDKManager.Instance.ComponentManager
                .GetFlightControllerHandler(0, 0).GetAircraftLocationAsync();
            height = await DJISDKManager.Instance.ComponentManager
                .GetFlightControllerHandler(0, 0).GetAltitudeAsync();
            //set antennaLocation
            aboveAntenna.location = place.value.Value;
            aboveAntenna.altitude = height.value.Value.value;
            System.Diagnostics.Debug.WriteLine("Done setting location");
        }
        /// <summary>
        /// After flying a safe distance away from the AUT at its elevation, this function is
        /// called to mark its elevation and calculate the minimum safe radius to fly around the antenna
        /// </summary>
        private async void setAntennaElevation(object sender, RoutedEventArgs e)
        {
            //height result and place result
            ResultValue<DoubleMsg?> height;
            ResultValue<LocationCoordinate2D?> place;
            //get elevation and location
            height = await DJISDKManager.Instance.ComponentManager
                .GetFlightControllerHandler(0, 0).GetAltitudeAsync();
            place = await DJISDKManager.Instance.ComponentManager
                .GetFlightControllerHandler(0, 0).GetAircraftLocationAsync();
            //set antenna elevation
            antennaElevation = height.value.Value.value;
            //calculate distance sqrt(a2+b2)
            double a = latToMeters(place.value.Value.latitude - aboveAntenna.location.latitude);
            double b = lonToMeters(place.value.Value.longitude - aboveAntenna.location.longitude);
            antennaMinRadius = Math.Sqrt(Math.Pow(a,2) + Math.Pow(b, 2));
            System.Diagnostics.Debug.WriteLine("Done setting elevation");
        }
        /// <summary>
        /// This function uses a unit circle to construct an N-gon shapen mission (currently
        /// an octagon) around the AUT in azimuth
        /// </summary>
        private void generateAzimuthMission(object sender, RoutedEventArgs e)
        {
            _currentMission = new WaypointMission()
            {
                waypointCount = 0,
                maxFlightSpeed = 10,
                autoFlightSpeed = flightSpeedAzimuth,
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

            LocationCoordinate2D[] locations = new LocationCoordinate2D[resolutionAzimuth + 1];
            int pointCount = 0;
            for (double i = 0; i <= 360; i += 360 / resolutionAzimuth)
            {
                //get the offet in (x-latitude,y-longitude) using sin and cos with a unit circle,
                //then multiplying by desired radius
                locations[pointCount] = makeCoordinate(
                    aboveAntenna.location.latitude + toLat(Math.Sin(i) * radius),//lat
                    aboveAntenna.location.longitude + toLon(Math.Cos(i) * radius));//lon
                pointCount++;
            }
            //add locations to azimuthMission
            DJI.WindowsSDK.Waypoint curr = new DJI.WindowsSDK.Waypoint();
            for (int i = 0; i < resolutionAzimuth + 1; i++)
            {
                curr = makeWaypoint(locations[i], antennaElevation);//elevation is set here, can be
                                                                    //antennaElevation or altitudeMaxGain
                curr.cornerRadiusInMeters = _cornerRadius;
                curr.speed = flightSpeedAzimuth;
                _currentMission.waypoints.Add(curr);
            }
            System.Diagnostics.Debug.WriteLine("Done making azimuth mission");
        }
        /// <summary>
        /// This function uses a unit circle to construct an N-gon shaped half-arc mission over
        /// the top half of the antenna.
        /// </summary>
        private void generateElevationMission(object sender, RoutedEventArgs e)
        {
            _currentMission = new WaypointMission()
            {
                waypointCount = 0,
                maxFlightSpeed = 10,
                autoFlightSpeed = flightSpeedElevation,
                finishedAction = WaypointMissionFinishedAction.NO_ACTION,
                headingMode = WaypointMissionHeadingMode.AUTO,
                flightPathMode = WaypointMissionFlightPathMode.CURVED,
                gotoFirstWaypointMode = WaypointMissionGotoFirstWaypointMode.SAFELY,
                exitMissionOnRCSignalLostEnabled = false,
                gimbalPitchRotationEnabled = true,
                repeatTimes = 0,
                missionID = 0,
                waypoints = new List<Waypoint>()
            };

            DJI.WindowsSDK.Waypoint[] locations = new DJI.WindowsSDK.Waypoint[resolutionElevation];
            int pointCount = 0;
            for (double i = 0; i <= 180; i += (180 / (resolutionElevation - 1)))
            {
                //get the offet in (x-latitude,y-longitude) using sin and cos with a unit circle,
                //then multiplying by desired radius
                locations[pointCount] = makeWaypoint(
                    makeCoordinate(
                        aboveAntenna.location.latitude + toLat(Math.Cos(i * Math.PI / 180) * radius),//lat
                        aboveAntenna.location.longitude),//lon
                        antennaElevation + (Math.Sin(i * Math.PI / 180) * radius));//elevation
                pointCount++;
            }
            //add locations to azimuthMission
            for (int i = 0; i < resolutionElevation; i++)
            {
                locations[i].cornerRadiusInMeters = _cornerRadius;
                locations[i].speed = flightSpeedElevation;
                _currentMission.waypoints.Add(locations[i]);
            }
        }
        /// <summary>
        /// This function enables groundstation mode, object avoidance, and vision assisted positioning
        /// </summary>
        private async void InitMission(object sender, RoutedEventArgs e)
        {
            //for error reporting
            DJI.WindowsSDK.SDKError resultcode;
            //set ground station mode to true, enable object avoidance, and vision assisted flight
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
        /// <summary>
        /// This function hands the currently constructed mission to the mission handler, then tells it
        /// to upload the mission to the drone.
        /// </summary>
        private async void UploadMission(object sender, RoutedEventArgs e)
        {
            //for error reporting
            DJI.WindowsSDK.SDKError resultcode;
            //give mission to class, upload to drone, start mission
            resultcode = DJISDKManager.Instance.WaypointMissionManager.GetWaypointMissionHandler(0)
                .LoadMission(_currentMission);
            System.Diagnostics.Debug.WriteLine(resultcode.ToString());
            await Task.Delay(3000);
            resultcode = await DJISDKManager.Instance.WaypointMissionManager.GetWaypointMissionHandler(0)
                .UploadMission();
            System.Diagnostics.Debug.WriteLine(resultcode.ToString());
            System.Diagnostics.Debug.WriteLine("Done uploading!");
        }
        /// <summary>
        /// This instructs the drone to start the mission is has in its memory.
        /// </summary>
        private async void ExecuteMission(object sender, RoutedEventArgs e)
        {
            DJI.WindowsSDK.SDKError resultcode;
            resultcode = await DJISDKManager.Instance.WaypointMissionManager.GetWaypointMissionHandler(0)
                .StartMission();
            System.Diagnostics.Debug.WriteLine(resultcode.ToString());
        }
        /// <summary>
        /// This function returns the state of the mission handler, and by extention the drone. 
        /// Very important to use frequently to effectively use this program.
        /// </summary>
        private void GetState(object sender, RoutedEventArgs e)
        {
            //Send .GetCurrentState() result to debug
            System.Diagnostics.Debug.WriteLine(
                DJISDKManager.Instance.WaypointMissionManager.GetWaypointMissionHandler(0)
                .GetCurrentState());
        }
        /// <summary>
        /// Stops the drone mid execution of a mission
        /// </summary>
        private async void StopMission(object sender, RoutedEventArgs e)
        {
            System.Diagnostics.Debug.WriteLine(
                await DJISDKManager.Instance.WaypointMissionManager.GetWaypointMissionHandler(0)
                .StopMission());
        }
        /// <summary>
        /// Forks a thread and starts logging azimuth data to a csv while the drone is flying an
        /// azimuth mission. Returns after stoplogging is called.
        /// </summary>
        private async void startLogging(object sender, RoutedEventArgs e)
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
                try
                {
                    pair += (await getAngle() + ",");
                    pair += (await getMagnitude());
                    System.Diagnostics.Debug.WriteLine(pair);
                    await Windows.Storage.FileIO.AppendTextAsync(azimuthFile, pair);
                    pair = "";
                    ////wait for time set by polling rate
                    await Task.Delay(pollRate);
                }
                catch
                {
                    System.Diagnostics.Debug.WriteLine("Something went wrong");
                }
            }
        }
        /// <summary>
        /// Forks a thread and starts logging elevation data to a csv while the drone is flying an
        /// elevation mission. Returns after stoplogging is called.
        /// </summary>
        private async void startLoggingElevation(object sender, RoutedEventArgs e)
        {
            System.Diagnostics.Debug.WriteLine("Started logging");
            // Create sample file; replace if exists.
            Windows.Storage.StorageFolder storageFolder =
                Windows.Storage.ApplicationData.Current.LocalFolder;
            Windows.Storage.StorageFile azimuthFile =
                await storageFolder.CreateFileAsync("elevationPlot.csv",
                    Windows.Storage.CreationCollisionOption.GenerateUniqueName);

            maxGain = -999;//reset maxGain
            string pair = "";
            string mag;
            ResultValue<DoubleMsg?> height;
            executing = true;
            while (executing)//keep logging till flag set false by stopExecuting
            {
                //get magnitude
                mag = await getMagnitude();
                System.Diagnostics.Debug.WriteLine(Convert.ToDouble(mag));
                //Update altitude of max gain and max gain
                if (maxGain < Convert.ToDouble(mag))
                {
                    height = await DJISDKManager.Instance.ComponentManager
                        .GetFlightControllerHandler(0, 0).GetAltitudeAsync();
                    altitudeMaxGain = height.value.Value.value;
                    maxGain = Convert.ToDouble(mag);
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
        /// <summary>
        /// Sets the flag telling the current logging function to stop
        /// </summary>
        private void stopLogging(object sender, RoutedEventArgs e)
        {
            executing = false;
            System.Diagnostics.Debug.WriteLine("Stopped logging");
        }
        /// <summary>
        /// Tells the drone to takeoff and hover about 1 meter in the air
        /// </summary>
        private async void AutoTakeoff(object sender, RoutedEventArgs e)//takeoff drone
        {
            //write result of StartTakeOffAsync() to debug console
            System.Diagnostics.Debug.WriteLine(
                await DJISDKManager.Instance.ComponentManager.GetFlightControllerHandler(0, 0)
                .StartTakeoffAsync());
        }

        //======================= These functions help the ones above =============================
        /// <summary>
        /// Registers the SDK with DJI over the web, you can make your own DJI developer account
        /// and register the program yourself if authentication is no longer working. Look on
        /// DJI's site for instructions
        /// </summary>
        private async void Instance_SDKRegistrationEvent(SDKRegistrationState state, SDKError resultCode)
        {
            if (resultCode == SDKError.NO_ERROR)
            {
                System.Diagnostics.Debug.WriteLine("Register app successfully.");

                //The product connection state will be updated when it changes here.
                DJISDKManager.Instance.ComponentManager.GetProductHandler(0)
                    .ProductTypeChanged += async delegate (object sender, ProductTypeMsg? value)
                {
                    await Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, async () =>
                    {
                        if (value != null && value?.value != ProductType.UNRECOGNIZED)
                        {
                            System.Diagnostics.Debug.WriteLine("The Aircraft is connected now.");
                            //You can load/display your pages according to the aircraft connection
                            //state here.
                        }
                        else
                        {
                            System.Diagnostics.Debug.WriteLine("The Aircraft is disconnected now.");
                            //You can hide your pages according to the aircraft connection state here
                            //, or show the connection tips to the users.
                        }
                    });
                };

                //If you want to get the latest product connection state manually, you can use the
                //following code
                var productType = (await DJISDKManager.Instance.ComponentManager
                    .GetProductHandler(0).GetProductTypeAsync()).value;
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
        /// <summary>
        /// This is a contructor for quickly making waypoint objects, given a location and height
        /// </summary>
        private DJI.WindowsSDK.Waypoint makeWaypoint(LocationCoordinate2D _location, double _height)
        {
            DJI.WindowsSDK.Waypoint waypoint = new DJI.WindowsSDK.Waypoint();
            waypoint.location = _location;
            waypoint.altitude = _height;
            waypoint.cornerRadiusInMeters = radius;
            //waypoint.speed = flightSpeed;
            return waypoint;
        }
        /// <summary>
        /// This is a contructor for quickly making waypoint objects given latitude longitude and height
        /// </summary>
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
        /// <summary>
        /// This is a constructor for quickly making a 2d coordinate object given lat and lon
        /// </summary>
        private LocationCoordinate2D makeCoordinate(double _lat, double _lon)
        {
            LocationCoordinate2D _location = new LocationCoordinate2D();
            _location.latitude = _lat;
            _location.longitude = _lon;
            return _location;
        }
        /// <summary>
        /// This function calculates the angle of the drone from the antenna from 0-360 degrees
        /// in an azimuth mission (NOT FOR ELEVATION MISSIONS). 0 degrees is north
        /// </summary>
        private async Task<double> getAngle()
        {
            //waypoint and its data
            ResultValue<LocationCoordinate2D?> place;

            //Get current latitude, longitude, and altitude
            place = await DJISDKManager.Instance.ComponentManager
                .GetFlightControllerHandler(0, 0).GetAircraftLocationAsync();

            //get two sides of triangle
            double x = latToMeters(place.value.Value.latitude - aboveAntenna.location.latitude);
            double y = lonToMeters(place.value.Value.longitude - aboveAntenna.location.longitude);

            //Right triangle, solve for angle, convert from radians to degrees
            if (x >= 0 && y >= 0)
            {//Q1
                return (180 / Math.PI) * Math.Atan(y / x);//90 to 180 degrees
            }
            else if (x < 0 && y >= 0)
            {//Q2
                return ((180 / Math.PI) * Math.Abs(Math.Atan(x / y))) + 90;//180 to 270 degrees
            }
            else if (x < 0 && y < 0)
            {//Q3
                return ((180 / Math.PI) * Math.Atan(y / x)) + 180;//270 to 360 dgrees
            }
            else if (x >= 0 && y < 0)
            {//Q4
                return ((180 / Math.PI) * Math.Abs(Math.Atan(x / y))) + 270;//0 to 90 degrees
            }
            else
            {
                return -1234567;
            }
        }
        /// <summary>
        /// This function calculates the angle of the drone from the antenna from 0-360 degrees
        /// in an elevation mission (NOT FOR AZIMUTH MISSIONS). 0 Degrees is north.
        /// </summary>
        private async Task<double> getAngleElevation()//used to calculate angle in logging elevation
        {
            //waypoint and its data
            ResultValue<LocationCoordinate2D?> place;
            ResultValue<DoubleMsg?> height;

            //Get current latitude, longitude, and altitude
            place = await DJISDKManager.Instance.ComponentManager
                .GetFlightControllerHandler(0, 0).GetAircraftLocationAsync();
            height = await DJISDKManager.Instance.ComponentManager
                .GetFlightControllerHandler(0, 0).GetAltitudeAsync();

            //get two sides of triangle
            double x = latToMeters(place.value.Value.latitude - aboveAntenna.location.latitude);
            //distance from antenna in lat
            double y = height.value.Value.value - antennaElevation;//distance from antenna in elevation

            //Right triangle, solve for angle, convert from radians to degrees
            if (x >= 0 && y >= 0)
            {//Q1
                return (180 / Math.PI) * Math.Atan(y / x);//90 to 180 degrees
            }
            else if (x < 0 && y >= 0)
            {//Q2
                return ((180 / Math.PI) * Math.Abs(Math.Atan(x / y))) + 90;//180 to 270 degrees
            }
            else if (x < 0 && y < 0)
            {//Q3
                return ((180 / Math.PI) * Math.Atan(y / x)) + 180;//270 to 360 dgrees
            }
            else if(x >= 0 && y < 0)
            {//Q4
                return ((180 / Math.PI) * Math.Abs(Math.Atan(x / y))) + 270;//0 to 90 degrees
            }
            else
            {
                return -1234567;
            }
            //return 60;
        }
        /// <summary>
        /// This function uses desktop link to communicate with the FullTrust program and get the
        /// magnitude from the spetrum analyzer. You must set the cursor on the spectrum analyzer
        /// to the frequency you want by pressing peak when the signal generator is on.
        /// </summary>
        private async Task<string> getMagnitude()
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
        }
        /// <summary>
        /// Converts meters to longitude, you will need to adjust this number for different geolocations
        /// </summary>
        private double toLon(double meters)
        {
            return meters * degreesInMeterLon;
        }
        /// <summary>
        /// Converts longitude to meters, you will need to adjust this number for different geolocations
        /// </summary>
        private double lonToMeters(double lon)
        {
            return lon * (1 / degreesInMeterLon);
        }
        /// <summary>
        /// Converts meters to latitude
        /// </summary>
        private double toLat(double meters)
        {
            return meters * degreesInMeterLat;
        }
        /// <summary>
        /// Converts latitude to meters, same at all geolocations
        /// </summary>
        private double latToMeters(double lat)
        {
            return lat * (1 / degreesInMeterLat);
        }
        //=========================== These are for testing purposes ==============================
        /// <summary>
        /// Takes the drones current location and adds it to the current missions list of points
        /// </summary>
        private async void AddWaypointAtCurrentLocation(object sender, RoutedEventArgs e)
        {
            //waypoint and its data
            ResultValue<LocationCoordinate2D?> place;
            ResultValue<DoubleMsg?> height;
            //Get current latitude, longitude, and altitude
            place = await DJISDKManager.Instance.ComponentManager
                .GetFlightControllerHandler(0, 0).GetAircraftLocationAsync();
            height = await DJISDKManager.Instance.ComponentManager
                .GetFlightControllerHandler(0, 0).GetAltitudeAsync();
            //Add waypoint
            DJI.WindowsSDK.Waypoint _waypoint = new DJI.WindowsSDK.Waypoint();
            _waypoint.location = place.value.Value;
            _waypoint.altitude = height.value.Value.value;
            _waypoint.cornerRadiusInMeters = 3;
            _currentMission.waypoints.Add(_waypoint);
            System.Diagnostics.Debug.WriteLine("added a waypoint at (" + _waypoint.location.latitude
                + ", " + _waypoint.location.longitude + ")");
        }
        /// <summary>
        /// Reports distance in lat/lon from the AUT location, useful for adjusting the lat/lon
        /// conversions.
        /// </summary>
        private async void oneMeterTestLat(object sender, RoutedEventArgs e)
        {
            //waypoint and its data
            ResultValue<LocationCoordinate2D?> place;
            ResultValue<DoubleMsg?> height;
            //Get current latitude, longitude, and altitude
            place = await DJISDKManager.Instance.ComponentManager
                .GetFlightControllerHandler(0, 0).GetAircraftLocationAsync();
            height = await DJISDKManager.Instance.ComponentManager
                .GetFlightControllerHandler(0, 0).GetAltitudeAsync();
            //Add waypoint
            DJI.WindowsSDK.Waypoint _waypoint = new DJI.WindowsSDK.Waypoint();
            _waypoint.location = place.value.Value;
            _waypoint.altitude = height.value.Value.value;

            double differenceLat = aboveAntenna.location.latitude - _waypoint.location.latitude;
            double differenceLon = aboveAntenna.location.longitude - _waypoint.location.longitude;

            System.Diagnostics.Debug.WriteLine("Difference in lat is " + differenceLat);
            System.Diagnostics.Debug.WriteLine("Difference in lon is " + differenceLon);
        }
        /// <summary>
        /// Flies 3 meters away from drones current location in latitude and longitude
        /// </summary>
        private void oneMeterTestLon(object sender, RoutedEventArgs e)
        {
            //Add antenna location
            _currentMission.waypoints.Add(aboveAntenna);
            //Make waypoint 1 meter away
            LocationCoordinate2D place = makeCoordinate(aboveAntenna.location.latitude
                + toLat(3), aboveAntenna.location.longitude + toLon(3));
            //Add waypoint at place, height
            _currentMission.waypoints.Add(makeWaypoint(place, aboveAntenna.altitude));
        }
        /// <summary>
        /// Reports back the drones current location and height in latitude, longitude, and meters
        /// </summary>
        private async void getLocation(object sender, RoutedEventArgs e)//prints location to debug terminal
        {
            DJI.WindowsSDK.ResultValue<LocationCoordinate2D?> place;
            ResultValue<DoubleMsg?> height;

            place = await DJISDKManager.Instance.ComponentManager
                .GetFlightControllerHandler(0, 0).GetAircraftLocationAsync();
            height = await DJISDKManager.Instance.ComponentManager
                .GetFlightControllerHandler(0, 0).GetAltitudeAsync();
            System.Diagnostics.Debug.WriteLine(place.value.Value.ToString());
            System.Diagnostics.Debug.WriteLine(height.value.Value.value);

        }
        /// <summary>
        /// Flies 9.144 meters north of AUT location twice
        /// </summary>
        private void testlat(object sender, RoutedEventArgs e)
        {
            _currentMission.waypoints.Add(makeWaypoint(makeCoordinate(aboveAntenna.location.latitude,
                aboveAntenna.location.longitude), antennaElevation));
            _currentMission.waypoints.Add(makeWaypoint(makeCoordinate(aboveAntenna.location.latitude
                + toLat(9.144), aboveAntenna.location.longitude), antennaElevation));
            _currentMission.waypoints.Add(makeWaypoint(makeCoordinate(aboveAntenna.location.latitude,
                aboveAntenna.location.longitude), antennaElevation));
            _currentMission.waypoints.Add(makeWaypoint(makeCoordinate(aboveAntenna.location.latitude
                + toLat(9.144), aboveAntenna.location.longitude), antennaElevation));
        }
        /// <summary>
        /// Flies 9.144 meters east of AUT location twice
        /// </summary>
        private void testlon(object sender, RoutedEventArgs e)
        {
            _currentMission.waypoints.Add(makeWaypoint(makeCoordinate(aboveAntenna.location.latitude,
                aboveAntenna.location.longitude), antennaElevation));
            _currentMission.waypoints.Add(makeWaypoint(makeCoordinate(aboveAntenna.location.latitude,
                aboveAntenna.location.longitude + toLon(9.144)), antennaElevation));
            _currentMission.waypoints.Add(makeWaypoint(makeCoordinate(aboveAntenna.location.latitude,
                aboveAntenna.location.longitude), antennaElevation));
            _currentMission.waypoints.Add(makeWaypoint(makeCoordinate(aboveAntenna.location.latitude,
                aboveAntenna.location.longitude + toLon(9.144)), antennaElevation));
        }

        //=========================== unused utility functions ====================================
        /// <summary>
        /// Sets the home location of the drone as its current location
        /// </summary>
        private async void SetHome(object sender, RoutedEventArgs e)
        {
            //send result of .SetHomeLocationUsingAircraftCurrentLocationAsync() to debug console
            System.Diagnostics.Debug.WriteLine(
                await DJISDKManager.Instance.ComponentManager.GetFlightControllerHandler(0, 0)
                .SetHomeLocationUsingAircraftCurrentLocationAsync());
        }
        /// <summary>
        /// Sends the drone to its home location
        /// </summary>
        private async void GoHome(object sender, RoutedEventArgs e)
        {
            //Send result of .StartGoHomeAsync() to debug
            System.Diagnostics.Debug.WriteLine(
                await DJISDKManager.Instance.ComponentManager.GetFlightControllerHandler(0, 0)
                .StartGoHomeAsync());
        }
        /// <summary>
        /// No idea how this drone functionality is done with the SDK. Try using the app to
        /// calibrate compass.
        /// </summary>
        private async void CalibrateCompass(object sender, RoutedEventArgs e)
        {
            System.Diagnostics.Debug.WriteLine(
                await DJISDKManager.Instance.ComponentManager.GetFlightControllerHandler(0, 0)
                .StartCompasCalibrationAsync());
        }
        /// <summary>
        /// Same idea as the last function, use the app to calibrate the IMU, no idea how this
        /// is supposed to be used, purely experimental.
        /// </summary>
        private async void CalibrateIMU(object sender, RoutedEventArgs e)
        {
            System.Diagnostics.Debug.WriteLine(
                await DJISDKManager.Instance.ComponentManager.GetFlightControllerHandler(0, 0)
                .StartIMUCalibrationAsync());
        }

        //========================= Desktop Link code, credit Stefan Wick =========================
        /// <summary>
        /// kick off the desktop process and listen to app service connection events
        /// </summary>
        protected async override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);

            if (ApiInformation.IsApiContractPresent("Windows.ApplicationModel.FullTrustAppContract", 1, 0))
            {
                App.AppServiceConnected += MainPage_AppServiceConnected;
                App.AppServiceDisconnected += MainPage_AppServiceDisconnected;
                await FullTrustProcessLauncher.LaunchFullTrustProcessForCurrentAppAsync();
            }
        }
        /// <summary>
        /// This is never used, but wont compile without the function
        /// When the desktop process is connected, get ready to receive requests
        /// </summary>
        private async void MainPage_AppServiceConnected(object sender, AppServiceTriggerDetails e)
        {
            await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
            {
                // enable UI to access  the connection
                //btnRegKey.IsEnabled = true;
            });
        }
        /// <summary>
        /// When the desktop process is disconnected, reconnect if needed
        /// </summary>
        private async void MainPage_AppServiceDisconnected(object sender, EventArgs e)
        {
            await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, ()=>
            {
                // disable UI to access the connection
                //btnRegKey.IsEnabled = false;

                // ask user if they want to reconnect
                Reconnect();
            });
        }
        /// <summary>
        /// This is never used, but wont compile without
        /// Ask user if they want to reconnect to the desktop process
        /// </summary>
        private async void Reconnect()
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