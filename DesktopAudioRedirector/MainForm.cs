using NAudio.CoreAudioApi;
using NAudio.Wave;
using System;
using System.Threading;
using System.Windows.Forms;

namespace DesktopAudioRedirector
{
	public partial class MainForm : Form
	{
		System.Threading.Timer BufferTimer { get; }

		public MainForm()
		{
			InitializeComponent();

			RefleshDevices();
			BufferTimer = new System.Threading.Timer(s =>
			{
				try
				{
					Invoke(new Action(() =>
					{
						label3.Text = $"Buffer: " + capture?.BufferLength;
					}));
				}
				catch (ObjectDisposedException) { }
			}, null, -1, -1);
		}

		WasapiCaptureWaveProvider capture;
		WasapiOut wout;

		private void RefleshDevices()
		{
			comboBox1.Items.Clear();
			comboBox2.Items.Clear();
			var ennumerator = new MMDeviceEnumerator();
			foreach (var point in ennumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active))
			{
				comboBox1.Items.Add(point);
				comboBox2.Items.Add(point);
			}
			foreach (var point in ennumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active))
				comboBox1.Items.Add(point);
			comboBox1.SelectedIndex = comboBox2.SelectedIndex = 0;

		}

		private void RefleshDevicesButtonClicked(object sender, EventArgs e)
		{
			RefleshDevices();
		}

		private void ControlButtonClicked(object sender, EventArgs e)
		{
			if ((wout?.PlaybackState ?? PlaybackState.Stopped) == PlaybackState.Playing)
			{
				BufferTimer.Change(-1, -1);
				label3.Text = "Buffer: -";
				wout?.Dispose();
				wout = null;
				capture?.Stop();
				capture = null;
				button1.Text = "開始";
				groupBox1.Enabled = true;
				button2.Enabled = false;
				button3.Enabled = true;
				return;
			}
			if (comboBox1.SelectedItem == comboBox2.SelectedItem)
			{
				MessageBox.Show("同じデバイスに入出力を設定することはできません。", null, MessageBoxButtons.OK, MessageBoxIcon.Error);
				return;
			}
			wout = new WasapiOut((MMDevice)comboBox2.SelectedItem, AudioClientShareMode.Shared, true, 1);
			capture = new WasapiCaptureWaveProvider((MMDevice)comboBox1.SelectedItem);
			wout.Init(capture);
			wout.Play();
			BufferTimer.Change(50, 50);
			button1.Text = "終了";
			groupBox1.Enabled = false;
			button2.Enabled = true;
			button3.Enabled = false;
		}

		private void ClearBufferButtonClicked(object sender, EventArgs e)
		{
			if (capture != null)
				capture.BufferLength = 0;
		}

		protected override void OnClosed(EventArgs e)
		{
			base.OnClosed(e);
			BufferTimer.Change(-1, -1);
			capture?.Stop();
			capture = null;
			wout?.Dispose();
			wout = null;
		}
	}
	public class WasapiCaptureWaveProvider : IWaveProvider
	{
		public WasapiCaptureWaveProvider(MMDevice device)
		{
			if (device.DataFlow == DataFlow.Render)
				Capture = new WasapiLoopbackCapture(device);
			else
				Capture = new WasapiCapture(device);
			WaveFormat = Capture.WaveFormat;
			Buffer = new byte[WaveFormat.BitsPerSample * WaveFormat.SampleRate / 2];
			Capture.DataAvailable += (s, e) =>
			{
				lock (LockObject)
				{
					if (BufferLength <= 0)
					{
						System.Buffer.BlockCopy(e.Buffer, 0, Buffer, 0, e.BytesRecorded);
						BufferLength = e.BytesRecorded;
					}
					else
					{
						// MEMO: 応急処置
						if (Buffer.Length < BufferLength + e.BytesRecorded)
							BufferLength = 0;

						System.Buffer.BlockCopy(e.Buffer, 0, Buffer, BufferLength, e.BytesRecorded);
						BufferLength += e.BytesRecorded;
					}
				}
				WritARE.Set();
			};
			Capture.StartRecording();
		}

		private bool FirstRead { get; set; } = true;
		private byte[] Buffer { get; }
		//private int BuffOffset { get; set; }
		public int BufferLength { get; set; }

		private WasapiCapture Capture { get; }
		public WaveFormat WaveFormat { get; }
		public bool Stopped { get; private set; }
		private object LockObject { get; } = new object();
		private AutoResetEvent WritARE { get; } = new AutoResetEvent(false);

		public int Read(byte[] buffer, int offset, int count)
		{
			if (Stopped)
				return 0;
			int readedOffset = 0;
			while (true)
			{
				lock (LockObject)
				{
					// Bluetoothなどでは初回出力まで時間がかかるため初回出力の場合バッファをリセットさせる
					if (FirstRead)
					{
						BufferLength = 0;
						FirstRead = false;
					}

					if (BufferLength > 0)
					{
						// 収まる場合
						if (BufferLength >= count - readedOffset)
						{
							System.Buffer.BlockCopy(Buffer, 0, buffer, readedOffset + offset, count - readedOffset);
							if (BufferLength == count - readedOffset)
							{
								BufferLength = 0;
								return count;
							}
							System.Buffer.BlockCopy(Buffer, count - readedOffset, Buffer, 0, BufferLength - (count - readedOffset));
							BufferLength -= count - readedOffset;
							return count;
						}
						// 収まらない場合
						System.Buffer.BlockCopy(Buffer, 0, buffer, offset + readedOffset, BufferLength);
						readedOffset += BufferLength;
						BufferLength = 0;
					}
				}
				WritARE.WaitOne();
			}
		}
		public void Stop()
		{
			Capture.StopRecording();
			Stopped = true;
			WritARE.Set();
		}
	}
}
