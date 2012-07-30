// Motion Detection sample application
// AForge.NET Framework
// http://www.aforgenet.com/framework/
//
// Copyright © AForge.NET, 2006-2012
// contacts@aforgenet.com
//

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Drawing.Imaging;
using System.Text;
using System.Windows.Forms;
using System.Threading;
using System.Linq;

using AForge;
using AForge.Imaging;
using AForge.Video;
using AForge.Video.VFW;
using AForge.Video.DirectShow;
using AForge.Vision.Motion;
using System.Runtime.InteropServices;
using SharedClasses;
using System.IO;
using System.Diagnostics;

namespace MotionDetectorSample
{
	public partial class MainForm : Form
	{
		private const string ThisAppName = "AForgeMotionDetector";
		private readonly TimeSpan DefaultSnapshotInterval = TimeSpan.FromSeconds(1);
		private readonly TimeSpan DefaultWaitNoUserinputDuration = TimeSpan.FromSeconds(10);
		private readonly TimeSpan DefaultKeepRecordingAfterLastMovementDuration = TimeSpan.FromMinutes(1);
		private string filePathForIntervalSeconds = SettingsInterop.GetFullFilePathInLocalAppdata("intervalforsnapshot_seconds.fjset", ThisAppName);
		private string filePathForWaitNoUserInputSeconds = SettingsInterop.GetFullFilePathInLocalAppdata("periodwaitnotuserinput_seconds.fjset", ThisAppName);
		private string filePathForKeepRecordingAfterLastMovement = SettingsInterop.GetFullFilePathInLocalAppdata("keeprecordingafterlastmovement_minutes.fjset", ThisAppName);
		private string filePathBulksmsUsername = SettingsInterop.GetFullFilePathInLocalAppdata("bulksms_username.fjset", ThisAppName);
		private string filePathBulksmsPassword = SettingsInterop.GetFullFilePathInLocalAppdata("bulksms_password.fjset", ThisAppName);
		private string filePathBulksmsNumbers = SettingsInterop.GetFullFilePathInLocalAppdata("bulksms_numbers.fjset", ThisAppName);
		private string filePathBulksmsPreformattedMessage = SettingsInterop.GetFullFilePathInLocalAppdata("bulksms_preformattedmessage.fjset", ThisAppName);
		private string filePathBulksmsPhotoBaseUrl = SettingsInterop.GetFullFilePathInLocalAppdata("bulksms_photobaseurl.fjset", ThisAppName);

		// opened video source
		private IVideoSource videoSource = null;
		// motion detector
		MotionDetector detector = new MotionDetector(
			new TwoFramesDifferenceDetector(),
			new MotionAreaHighlighting());
		// motion detection and processing algorithm
		private int motionDetectionType = 1;
		private int motionProcessingType = 1;

		// statistics length
		private const int statLength = 15;
		// current statistics index
		private int statIndex = 0;
		// ready statistics values
		private int statReady = 0;
		// statistics array
		private int[] statCount = new int[statLength];

		// counter used for flashing
		private int flash = 0;
		private float motionAlarmLevel = 0.015f;

		private List<float> motionHistory = new List<float>();
		private int detectedObjectsCount = -1;

		// Constructor
		public MainForm()
		{
			InitializeComponent();
			Application.Idle += new EventHandler(Application_Idle);

			appStartupTime = DateTime.Now;

			LoadSnapshotInterval(filePathForIntervalSeconds, DefaultSnapshotInterval, numericUpDownSnapshotInterval, "snapshot interval", true);
			numericUpDownSnapshotInterval.ValueChanged += delegate { SaveSnapshotInterval(filePathForIntervalSeconds, numericUpDownSnapshotInterval, "snapshot interval"); };

			//LoadSnapshotInterval(filePathForWaitNoUserInputSeconds, DefaultWaitNoUserinputDuration, numericUpDownWaitNoUserInput, "wait for no userinput duration", true);
			//numericUpDownWaitNoUserInput.ValueChanged += delegate { SaveSnapshotInterval(filePathForWaitNoUserInputSeconds, numericUpDownWaitNoUserInput, "wait for no userinput duration"); };

			LoadSnapshotInterval(filePathForKeepRecordingAfterLastMovement, DefaultKeepRecordingAfterLastMovementDuration, numericUpDownKeepRecordingAfterLastMovement, "keep recording after last movement duration", false);
			numericUpDownKeepRecordingAfterLastMovement.ValueChanged += delegate { SaveSnapshotInterval(filePathForKeepRecordingAfterLastMovement, numericUpDownKeepRecordingAfterLastMovement, "keep recording after last movement duration"); };
		}

		private string Bulksms_username { get { return File.ReadAllText(filePathBulksmsUsername).Trim(); } }
		private string Bulksms_password { get { return File.ReadAllText(filePathBulksmsPassword).Trim(); } }
		private List<string> Bulksms_numbers { get { return File.ReadAllLines(filePathBulksmsNumbers).Where(num => !string.IsNullOrWhiteSpace(num)).ToList(); } }
		private string Bulksms_preformattedMessage { get { return File.ReadAllText(filePathBulksmsPreformattedMessage).Trim(); } }
		private string Bulksms_baseurl { get { return File.ReadAllText(filePathBulksmsPhotoBaseUrl).Trim(); } }

		private static void LoadSnapshotInterval(string filepath, TimeSpan defaultValue, NumericUpDown numericUpDown, string intervalname_formessages, bool secondstrue_minutesfalse)
		{
			if (!File.Exists(filepath))
			{
				numericUpDown.Value = secondstrue_minutesfalse ? (int)defaultValue.TotalSeconds : (int)defaultValue.TotalMinutes;
				SaveSnapshotInterval(filepath, numericUpDown, intervalname_formessages);
				return;
			}

			int tmpIntervalSecondsOrMinutes;
			try
			{
				if (int.TryParse(File.ReadAllText(filepath), out tmpIntervalSecondsOrMinutes))
					numericUpDown.Value = tmpIntervalSecondsOrMinutes;
			}
			catch (Exception exc)
			{
				UserMessages.ShowErrorMessage("Unable to read " + intervalname_formessages + " from file: " + exc.Message);
			}
		}
		private static void SaveSnapshotInterval(string filepath, NumericUpDown numericUpDown, string intervalname_formessages)
		{
			try
			{
				File.WriteAllText(filepath, numericUpDown.Value.ToString());
			}
			catch (Exception exc)
			{
				UserMessages.ShowErrorMessage("Unable to save new " + intervalname_formessages + ": " + exc.Message);
			}
		}

		private void MainForm_Load(object sender, EventArgs e)
		{
			WindowMessagesInterop.InitializeClientMessages();
			OpenFirstCamera();
		}

		protected override void WndProc(ref Message m)
		{
			WindowMessagesInterop.MessageTypes mt;
			WindowMessagesInterop.ClientHandleMessage(m.Msg, m.WParam, m.LParam, out mt);
			if (mt == WindowMessagesInterop.MessageTypes.Show)
			{
				if (this.WindowState == FormWindowState.Minimized)
					this.WindowState = FormWindowState.Normal;
				this.Show();
			}
			else if (mt == WindowMessagesInterop.MessageTypes.Hide)
				this.Hide();
			else if (mt == WindowMessagesInterop.MessageTypes.Close)
				this.Close();
			else
				base.WndProc(ref m);
		}

		// Application's main form is closing
		private void MainForm_FormClosing(object sender, FormClosingEventArgs e)
		{
			CloseVideoSource();
		}

		// "Exit" menu item clicked
		private void exitToolStripMenuItem_Click(object sender, EventArgs e)
		{
			this.Close();
		}

		// "About" menu item clicked
		private void aboutToolStripMenuItem_Click(object sender, EventArgs e)
		{
			AboutForm form = new AboutForm();
			form.ShowDialog();
		}

		// "Open" menu item clieck - open AVI file
		private void openToolStripMenuItem_Click(object sender, EventArgs e)
		{
			if (openFileDialog.ShowDialog() == DialogResult.OK)
			{
				// create video source
				AVIFileVideoSource fileSource = new AVIFileVideoSource(openFileDialog.FileName);

				OpenVideoSource(fileSource);
			}
		}

		// Open JPEG URL
		private void openJPEGURLToolStripMenuItem_Click(object sender, EventArgs e)
		{
			URLForm form = new URLForm();

			form.Description = "Enter URL of an updating JPEG from a web camera:";
			form.URLs = new string[]
				{
					"http://195.243.185.195/axis-cgi/jpg/image.cgi?camera=1"
				};

			if (form.ShowDialog(this) == DialogResult.OK)
			{
				// create video source
				JPEGStream jpegSource = new JPEGStream(form.URL);

				// open it
				OpenVideoSource(jpegSource);
			}
		}

		// Open MJPEG URL
		private void openMJPEGURLToolStripMenuItem_Click(object sender, EventArgs e)
		{
			URLForm form = new URLForm();

			form.Description = "Enter URL of an MJPEG video stream:";
			form.URLs = new string[]
				{
					"http://195.243.185.195/axis-cgi/mjpg/video.cgi?camera=3",
					"http://195.243.185.195/axis-cgi/mjpg/video.cgi?camera=4",
					"http://192.168.1.100:8081/videofeed"
				};

			if (form.ShowDialog(this) == DialogResult.OK)
			{
				// create video source
				MJPEGStream mjpegSource = new MJPEGStream(form.URL);

				// open it
				OpenVideoSource(mjpegSource);
			}
		}

		// Open local video capture device
		private void localVideoCaptureDeviceToolStripMenuItem_Click(object sender, EventArgs e)
		{
			VideoCaptureDeviceForm form = new VideoCaptureDeviceForm();

			if (form.ShowDialog(this) == DialogResult.OK)
			{
				// create video source
				VideoCaptureDevice videoSource = new VideoCaptureDevice(form.VideoDevice);

				// open it
				OpenVideoSource(videoSource);
			}
		}

		private void OpenFirstCamera()
		{
			string firstCamMonikerString = VideoCaptureDeviceForm.FirstCamera;
			if (string.IsNullOrEmpty(firstCamMonikerString))
				MessageBox.Show("No camera device found", "No camera", MessageBoxButtons.OK, MessageBoxIcon.Warning);
			else
				OpenVideoSource(new VideoCaptureDevice(firstCamMonikerString));
		}

		// Open video file using DirectShow
		private void openVideoFileusingDirectShowToolStripMenuItem_Click(object sender, EventArgs e)
		{
			if (openFileDialog.ShowDialog() == DialogResult.OK)
			{
				// create video source
				FileVideoSource fileSource = new FileVideoSource(openFileDialog.FileName);

				// open it
				OpenVideoSource(fileSource);
			}
		}

		// Open video source
		private void OpenVideoSource(IVideoSource source)
		{
			// set busy cursor
			this.Cursor = Cursors.WaitCursor;

			// close previous video source
			CloseVideoSource();

			// start new video source
			videoSourcePlayer.VideoSource = new AsyncVideoSource(source);
			videoSourcePlayer.Start();

			// reset statistics
			statIndex = statReady = 0;

			// start timers
			timer.Start();
			alarmTimer.Start();

			videoSource = source;

			this.Cursor = Cursors.Default;
		}

		// Close current video source
		private void CloseVideoSource()
		{
			// set busy cursor
			this.Cursor = Cursors.WaitCursor;

			// stop current video source
			videoSourcePlayer.SignalToStop();

			// wait 2 seconds until camera stops
			for (int i = 0; (i < 50) && (videoSourcePlayer.IsRunning); i++)
			{
				Thread.Sleep(100);
			}
			if (videoSourcePlayer.IsRunning)
				videoSourcePlayer.Stop();

			// stop timers
			timer.Stop();
			alarmTimer.Stop();

			motionHistory.Clear();

			// reset motion detector
			if (detector != null)
				detector.Reset();

			videoSourcePlayer.BorderColor = Color.Black;
			this.Cursor = Cursors.Default;
		}

		private string GetFolderpathForSnapshot()
		{
			string dir = SettingsInterop.LocalAppdataPath(ThisAppName);
			dir += "\\MotionDetected";
			if (!Directory.Exists(dir))
				Directory.CreateDirectory(dir);
			return dir;
		}

		private DateTime lastSnapshotTime = DateTime.Now;
		private DateTime lastmovementDetected = DateTime.MinValue;
		private DateTime lastSmsSend = DateTime.MinValue;
		private DateTime appStartupTime = DateTime.MinValue;
		private void TakeSnapshot(bool force = false)
		{
			DateTime now = DateTime.Now;

			if (now.Subtract(appStartupTime).TotalMinutes < 1)
				return;

			try
			{
				//bool mustTakeSnapshotBasedOnTimers = ;
				if (force || now.Subtract(lastSnapshotTime).TotalSeconds > (int)numericUpDownSnapshotInterval.Value)//&& IdleSeconds >= (int)numericUpDownWaitNoUserInput.Value//mustTakeSnapshotBasedOnTimers)
				{

					//ProwlAPI.SendNotificationUntilResponseFromiDevice(
					//		ProwlAPI.DefaultApiKey,
					//		"motiondetected",
					//		TimeSpan.FromSeconds(30),//Interval to for sending notification to iphone
					//		ProwlAPI.Priority.Emergency);
					new Thread(new ThreadStart(delegate
					{
						try
						{
							string filename = now.ToString("yyyy_MM_dd_HH_mm_ss") + ".jpeg";
							videoSourcePlayer.GetCurrentVideoFrame().Save(
								GetFolderpathForSnapshot() + "\\" + filename,
								//SettingsInterop.GetFullFilePathInLocalAppdata(now.ToString("yyyy MM dd HH mm ss") + ".jpeg", "AForgeMotionDetector", "MotionDetected"),
								ImageFormat.Jpeg);
							if (now.Subtract(lastSmsSend).TotalSeconds > TimeSpan.FromMinutes(30).TotalSeconds)//(int)numericUpDownSnapshotInterval.Value
							{
								string url = Bulksms_baseurl.TrimEnd('/') + "/" + filename;
								lastSmsSend = now;
								foreach (string num in Bulksms_numbers)
									SMS.SendSMS.SendMessageAndRetryIfFail(
										Bulksms_username,
										Bulksms_password,
										num,
										string.Format(Bulksms_preformattedMessage, now, url));
							}
						}
						catch { }
					})).Start();

					lastSnapshotTime = now;
					//if (mustTakeSnapshotBasedOnTimers)
					//	lastmovementDetected = now;
				}
			}
			catch { }
		}

		//private DateTime lastMovement = DateTime.MinValue;
		// New frame received by the player
		private void videoSourcePlayer_NewFrame(object sender, ref Bitmap image)
		{
			lock (this)
			{
				if (detector != null)
				{
					float motionLevel = detector.ProcessFrame(image);

					if (motionLevel > motionAlarmLevel)
					{
						//lastMovement = DateTime.Now;
						// flash for 2 seconds
						lastmovementDetected = DateTime.Now;
						flash = (int)(2 * (1000 / alarmTimer.Interval));
						TakeSnapshot();
					}
					else if (DateTime.Now.Subtract(lastmovementDetected).TotalMinutes <= (double)numericUpDownKeepRecordingAfterLastMovement.Value)
						TakeSnapshot(true);

					// check objects' count
					if (detector.MotionProcessingAlgorithm is BlobCountingObjectsProcessing)
					{
						BlobCountingObjectsProcessing countingDetector = (BlobCountingObjectsProcessing)detector.MotionProcessingAlgorithm;
						detectedObjectsCount = countingDetector.ObjectsCount;
					}
					else
					{
						detectedObjectsCount = -1;
					}

					// accumulate history
					motionHistory.Add(motionLevel);
					if (motionHistory.Count > 300)
					{
						motionHistory.RemoveAt(0);
					}

					if (showMotionHistoryToolStripMenuItem.Checked)
						DrawMotionHistory(image);
				}
			}
		}

		// Update some UI elements
		private void Application_Idle(object sender, EventArgs e)
		{
			objectsCountLabel.Text = (detectedObjectsCount < 0) ? string.Empty : "Objects: " + detectedObjectsCount;
		}

		// Draw motion history
		private void DrawMotionHistory(Bitmap image)
		{
			Color greenColor = Color.FromArgb(128, 0, 255, 0);
			Color yellowColor = Color.FromArgb(128, 255, 255, 0);
			Color redColor = Color.FromArgb(128, 255, 0, 0);

			BitmapData bitmapData = image.LockBits(new Rectangle(0, 0, image.Width, image.Height),
				ImageLockMode.ReadWrite, image.PixelFormat);

			int t1 = (int)(motionAlarmLevel * 500);
			int t2 = (int)(0.075 * 500);

			for (int i = 1, n = motionHistory.Count; i <= n; i++)
			{
				int motionBarLength = (int)(motionHistory[n - i] * 500);

				if (motionBarLength == 0)
					continue;

				if (motionBarLength > 50)
					motionBarLength = 50;

				Drawing.Line(bitmapData,
					new IntPoint(image.Width - i, image.Height - 1),
					new IntPoint(image.Width - i, image.Height - 1 - motionBarLength),
					greenColor);

				if (motionBarLength > t1)
				{
					Drawing.Line(bitmapData,
						new IntPoint(image.Width - i, image.Height - 1 - t1),
						new IntPoint(image.Width - i, image.Height - 1 - motionBarLength),
						yellowColor);
				}

				if (motionBarLength > t2)
				{
					Drawing.Line(bitmapData,
						new IntPoint(image.Width - i, image.Height - 1 - t2),
						new IntPoint(image.Width - i, image.Height - 1 - motionBarLength),
						redColor);
				}
			}

			image.UnlockBits(bitmapData);
		}

		// On timer event - gather statistics
		private void timer_Tick(object sender, EventArgs e)
		{
			IVideoSource videoSource = videoSourcePlayer.VideoSource;

			if (videoSource != null)
			{
				// get number of frames for the last second
				statCount[statIndex] = videoSource.FramesReceived;

				// increment indexes
				if (++statIndex >= statLength)
					statIndex = 0;
				if (statReady < statLength)
					statReady++;

				float fps = 0;

				// calculate average value
				for (int i = 0; i < statReady; i++)
				{
					fps += statCount[i];
				}
				fps /= statReady;

				statCount[statIndex] = 0;

				fpsLabel.Text = fps.ToString("F2") + " fps";
			}
		}

		// Turn off motion detection
		private void noneToolStripMenuItem1_Click(object sender, EventArgs e)
		{
			motionDetectionType = 0;
			SetMotionDetectionAlgorithm(null);
		}

		// Set Two Frames Difference motion detection algorithm
		private void twoFramesDifferenceToolStripMenuItem_Click(object sender, EventArgs e)
		{
			motionDetectionType = 1;
			SetMotionDetectionAlgorithm(new TwoFramesDifferenceDetector());
		}

		// Set Simple Background Modeling motion detection algorithm
		private void simpleBackgroundModelingToolStripMenuItem_Click(object sender, EventArgs e)
		{
			motionDetectionType = 2;
			SetMotionDetectionAlgorithm(new SimpleBackgroundModelingDetector(true, true));
		}

		// Turn off motion processing
		private void noneToolStripMenuItem2_Click(object sender, EventArgs e)
		{
			motionProcessingType = 0;
			SetMotionProcessingAlgorithm(null);
		}

		// Set motion area highlighting
		private void motionAreaHighlightingToolStripMenuItem_Click(object sender, EventArgs e)
		{
			motionProcessingType = 1;
			SetMotionProcessingAlgorithm(new MotionAreaHighlighting());
		}

		// Set motion borders highlighting
		private void motionBorderHighlightingToolStripMenuItem_Click(object sender, EventArgs e)
		{
			motionProcessingType = 2;
			SetMotionProcessingAlgorithm(new MotionBorderHighlighting());
		}

		// Set objects' counter
		private void blobCountingToolStripMenuItem_Click(object sender, EventArgs e)
		{
			motionProcessingType = 3;
			SetMotionProcessingAlgorithm(new BlobCountingObjectsProcessing());
		}

		// Set grid motion processing
		private void gridMotionAreaProcessingToolStripMenuItem_Click(object sender, EventArgs e)
		{
			motionProcessingType = 4;
			SetMotionProcessingAlgorithm(new GridMotionAreaProcessing(32, 32));
		}

		// Set new motion detection algorithm
		private void SetMotionDetectionAlgorithm(IMotionDetector detectionAlgorithm)
		{
			lock (this)
			{
				detector.MotionDetectionAlgorithm = detectionAlgorithm;
				motionHistory.Clear();

				if (detectionAlgorithm is TwoFramesDifferenceDetector)
				{
					if (
						(detector.MotionProcessingAlgorithm is MotionBorderHighlighting) ||
						(detector.MotionProcessingAlgorithm is BlobCountingObjectsProcessing))
					{
						motionProcessingType = 1;
						SetMotionProcessingAlgorithm(new MotionAreaHighlighting());
					}
				}
			}
		}

		// Set new motion processing algorithm
		private void SetMotionProcessingAlgorithm(IMotionProcessing processingAlgorithm)
		{
			lock (this)
			{
				detector.MotionProcessingAlgorithm = processingAlgorithm;
			}
		}

		// Motion menu is opening
		private void motionToolStripMenuItem_DropDownOpening(object sender, EventArgs e)
		{
			ToolStripMenuItem[] motionDetectionItems = new ToolStripMenuItem[]
            {
                noneToolStripMenuItem1, twoFramesDifferenceToolStripMenuItem,
                simpleBackgroundModelingToolStripMenuItem
            };
			ToolStripMenuItem[] motionProcessingItems = new ToolStripMenuItem[]
            {
                noneToolStripMenuItem2, motionAreaHighlightingToolStripMenuItem,
                motionBorderHighlightingToolStripMenuItem, blobCountingToolStripMenuItem,
                gridMotionAreaProcessingToolStripMenuItem
            };

			for (int i = 0; i < motionDetectionItems.Length; i++)
			{
				motionDetectionItems[i].Checked = (i == motionDetectionType);
			}
			for (int i = 0; i < motionProcessingItems.Length; i++)
			{
				motionProcessingItems[i].Checked = (i == motionProcessingType);
			}

			// enable/disable some motion processing algorithm depending on detection algorithm
			bool enabled = (motionDetectionType != 1);
			motionBorderHighlightingToolStripMenuItem.Enabled = enabled;
			blobCountingToolStripMenuItem.Enabled = enabled;
		}

		// On "Define motion regions" menu item selected
		private void defineMotionregionsToolStripMenuItem_Click(object sender, EventArgs e)
		{
			if (videoSourcePlayer.VideoSource != null)
			{
				Bitmap currentVideoFrame = videoSourcePlayer.GetCurrentVideoFrame();

				if (currentVideoFrame != null)
				{
					MotionRegionsForm form = new MotionRegionsForm();
					form.VideoFrame = currentVideoFrame;
					form.MotionRectangles = detector.MotionZones;

					// show the dialog
					if (form.ShowDialog(this) == DialogResult.OK)
					{
						Rectangle[] rects = form.MotionRectangles;

						if (rects.Length == 0)
							rects = null;

						detector.MotionZones = rects;
					}

					return;
				}
			}

			MessageBox.Show("It is required to start video source and receive at least first video frame before setting motion zones.",
				"Message", MessageBoxButtons.OK, MessageBoxIcon.Information);
		}

		// On opening of Tools menu
		private void toolsToolStripMenuItem_DropDownOpening(object sender, EventArgs e)
		{
			localVideoCaptureSettingsToolStripMenuItem.Enabled =
				((videoSource != null) && (videoSource is VideoCaptureDevice));
			crossbarVideoSettingsToolStripMenuItem.Enabled =
				((videoSource != null) && (videoSource is VideoCaptureDevice) && (videoSource.IsRunning));
		}

		// Display properties of local capture device
		private void localVideoCaptureSettingsToolStripMenuItem_Click(object sender, EventArgs e)
		{
			if ((videoSource != null) && (videoSource is VideoCaptureDevice))
			{
				try
				{
					((VideoCaptureDevice)videoSource).DisplayPropertyPage(this.Handle);
				}
				catch (NotSupportedException ex)
				{
					MessageBox.Show(ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
				}
			}
		}

		// Display properties of crossbar filter
		private void crossbarVideoSettingsToolStripMenuItem_Click(object sender, EventArgs e)
		{
			if ((videoSource != null) && (videoSource is VideoCaptureDevice) && (videoSource.IsRunning))
			{
				Console.WriteLine("Current input: " + ((VideoCaptureDevice)videoSource).CrossbarVideoInput);

				try
				{
					((VideoCaptureDevice)videoSource).DisplayCrossbarPropertyPage(this.Handle);
				}
				catch (NotSupportedException ex)
				{
					MessageBox.Show(ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
				}
			}
		}

		// Timer used for flashing in the case if motion is detected
		private void alarmTimer_Tick(object sender, EventArgs e)
		{
			if (flash != 0)
			{
				videoSourcePlayer.BorderColor = (flash % 2 == 1) ? Color.Black : Color.Red;
				flash--;
			}
		}

		// Change status of menu item when it is clicked
		private void showMotionHistoryToolStripMenuItem_Click(object sender, EventArgs e)
		{
			showMotionHistoryToolStripMenuItem.Checked = !showMotionHistoryToolStripMenuItem.Checked;
		}

		// Unmanaged function from user32.dll
		[DllImport("user32.dll")]
		static extern bool GetLastInputInfo(ref LASTINPUTINFO plii);

		// Struct we'll need to pass to the function
		internal struct LASTINPUTINFO
		{
			public uint cbSize;
			public uint dwTime;
		}

		//private static int IdleSeconds = 0;
		private void tmrIdle_Tick(object sender, EventArgs e)
		{
			int IdleSecondsNotUsed;
			//GetIdleSeconds();
		}

		/*private static void GetIdleSeconds()
		{
			// Get the system uptime
			int systemUptime = Environment.TickCount;
			// The tick at which the last input was recorded
			int LastInputTicks = 0;
			// The number of ticks that passed since last input
			int IdleTicks = 0;

			// Set the struct
			LASTINPUTINFO LastInputInfo = new LASTINPUTINFO();
			LastInputInfo.cbSize = (uint)Marshal.SizeOf(LastInputInfo);
			LastInputInfo.dwTime = 0;

			// If we have a value from the function
			if (GetLastInputInfo(ref LastInputInfo))
			{
				// Get the number of ticks at the point when the last activity was seen
				LastInputTicks = (int)LastInputInfo.dwTime;
				// Number of idle ticks = system uptime ticks - number of ticks at last input
				IdleTicks = systemUptime - LastInputTicks;
			}

			IdleSeconds = (int)(IdleTicks / 1000);
			//// Set the labels; divide by 1000 to transform the milliseconds to seconds
			//lblSystemUptime.Text = Convert.ToString(systemUptime / 1000) + " seconds";
			//lblIdleTime.Text = Convert.ToString(IdleTicks / 1000) + " seconds";
			//lblLastInput.Text = "At second " + Convert.ToString(LastInputTicks / 1000);
		}*/

		private void openSavedimagesFolderToolStripMenuItem_Click(object sender, EventArgs e)
		{
			Process.Start("explorer", GetFolderpathForSnapshot());
		}

		private void MainForm_Shown(object sender, EventArgs e)
		{
			bool bulkSmsAllExists = true;
			if (!File.Exists(filePathBulksmsUsername))
			{
				string un = Microsoft.VisualBasic.Interaction.InputBox("Please enter the BulkSMS username");
				if (string.IsNullOrWhiteSpace(un))
					bulkSmsAllExists = false;
				else
					File.WriteAllText(filePathBulksmsUsername, un);
			}
			else if (!File.Exists(filePathBulksmsPassword))
			{
				string pa = Microsoft.VisualBasic.Interaction.InputBox("Please enter the BulkSMS password");
				if (string.IsNullOrWhiteSpace(pa))
					bulkSmsAllExists = false;
				else
					File.WriteAllText(filePathBulksmsPassword, pa);
			}
			else if (!File.Exists(filePathBulksmsNumbers))
			{
				string nums = Microsoft.VisualBasic.Interaction.InputBox("Please enter the number to SMS to");
				if (string.IsNullOrWhiteSpace(nums))
					bulkSmsAllExists = false;
				else
					File.WriteAllText(filePathBulksmsNumbers, nums);
			}
			else if (!File.Exists(filePathBulksmsPreformattedMessage))
			{
				string msg = Microsoft.VisualBasic.Interaction.InputBox("Please enter the PRE-formatted message, containing parameters for 'Current time' and 'Url for photo'");
				if (string.IsNullOrWhiteSpace(msg))
					bulkSmsAllExists = false;
				else
					File.WriteAllText(filePathBulksmsPreformattedMessage, msg);
			}
			else if (!File.Exists(filePathBulksmsPhotoBaseUrl))
			{
				string baseurl = Microsoft.VisualBasic.Interaction.InputBox("Please enter the base url for photos");
				if (string.IsNullOrWhiteSpace(baseurl))
					bulkSmsAllExists = false;
				else
					File.WriteAllText(filePathBulksmsPhotoBaseUrl, baseurl);
			}

			if (!bulkSmsAllExists)
			{
				UserMessages.ShowErrorMessage("All settings are required for the program to work, now exiting");
				Application.Exit();
			}
		}
	}
}