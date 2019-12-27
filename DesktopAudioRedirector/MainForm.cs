using NAudio.CoreAudioApi;
using NAudio.Wave;
using System;
using System.Linq;
using System.Runtime.InteropServices;
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

			var endPoints = new MMDeviceEnumerator().EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active).ToArray();
			foreach (var point in endPoints)
			{
				comboBox1.Items.Add(point);
				comboBox2.Items.Add(point);
			}
			comboBox1.SelectedIndex = comboBox2.SelectedIndex = 0;

			BufferTimer = new System.Threading.Timer(s => Invoke(new Action(() =>
			{
				label3.Text = $"Buffer: " + capture?.BufferLength;
			})), null, -1, -1);
		}

		WasapiLoopbackCaptureWaveProvider capture;
		WasapiOut wout;

		private void ControlButtonClicked(object sender, EventArgs e)
		{
			if ((wout?.PlaybackState ?? PlaybackState.Stopped) == PlaybackState.Playing)
			{
				BufferTimer.Change(-1, -1);
				label3.Text = "Buffer: -";
				capture?.Stop();
				capture = null;
				wout?.Dispose();
				wout = null;
				button1.Text = "開始";
				groupBox1.Enabled = true;
				return;
			}
			if (comboBox1.SelectedItem == comboBox2.SelectedItem)
			{
				MessageBox.Show("同じデバイスに入出力を設定することはできません。", null, MessageBoxButtons.OK, MessageBoxIcon.Error);
				return;
			}
			wout = new WasapiOut((MMDevice)comboBox2.SelectedItem, AudioClientShareMode.Shared, true, 1);
			capture = new WasapiLoopbackCaptureWaveProvider((MMDevice)comboBox1.SelectedItem);
			wout.Init(capture);
			wout.Play();
			BufferTimer.Change(100, 100);
			button1.Text = "終了";
			groupBox1.Enabled = false;
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
	public class WasapiLoopbackCaptureWaveProvider : IWaveProvider
	{
		public WasapiLoopbackCaptureWaveProvider(MMDevice device)
		{
			Capture = new WasapiLoopbackCapture(device);
			WaveFormat = Capture.WaveFormat;
			Buffer = new byte[WaveFormat.BitsPerSample * WaveFormat.SampleRate];
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
						//var tb = new byte[Buffer.Length + e.BytesRecorded];
						//System.Buffer.BlockCopy(Buffer, 0, tb, 0, Buffer.Length);
						// TODO: オーバーランチェック
						System.Buffer.BlockCopy(e.Buffer, 0, Buffer, BufferLength, e.BytesRecorded);
						BufferLength += e.BytesRecorded;
					}
				}
				WritARE.Set();
			};
			Capture.StartRecording();
		}

		private byte[] Buffer { get; }
		//private int BuffOffset { get; set; }
		public int BufferLength { get; set; }

		private WasapiLoopbackCapture Capture { get; }
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
