# SteppIRDrone
This code interfaces with a DJI Phantom 4 Pro V2 done and a RIGOL DSA815 spectrum analyzer to
carry a payload transmit antenna around a receiving antenna to plot the receiving antennas (AUTs)
radiation pattern in azimuth at the elevation of highest gain. 

Build all three projects and deploy the Package project in VS build settings. Package runs UWP and FullTrust.
The UWP application contains the GUI and nearly all functionality, FullTrust just comminucates with the spectrum analyzer
and sends its data to the UWP app.

CREDIT: desktop link code is heavily reused from Stefan Wick's UWP with Desktop Extension tutorial
/https://stefanwick.com/2018/04/16/uwp-with-desktop-extension-part-3/
![test2](https://user-images.githubusercontent.com/81589499/197445034-abb8b4cd-7d42-4e72-ae94-4a87c0b5f5d0.PNG)
