using System;
using System.Windows.Forms;

namespace Vorrennung
{
    public partial class Zeitfinder : Form
    {
        Infographikfenster f;
        int gesdauer;
        public Zeitfinder(int dauergesammt,Infographikfenster f)
        {

            InitializeComponent();
            this.f = f;
            changeDuration(dauergesammt);
        }
        public void changeDuration(int dauergesammt)
        {
            gesdauer = dauergesammt;
            trackBar1.Maximum = dauergesammt;
            trackBar2.Maximum = dauergesammt;
            trackBar1.TickFrequency = dauergesammt / 20;
            trackBar2.TickFrequency = dauergesammt / 20;
            trackBar2.Minimum = Math.Min(dauergesammt, 20);
            label1.Text = $"Startzeit: {getTimeCode(trackBar1.Value)}";
            label2.Text = $"Dauer: {getTimeCode(trackBar2.Value)}";
            setMarker();
        }
        public int dauer => trackBar2.Value;
        public int startzeit => trackBar1.Value;
        public bool gueltig;
        public bool fertig;
        public void setPercent(double start,double dauer)
        {
            trackBar1.Value = (int)(start * trackBar1.Maximum);
            trackBar2.Value = (int)(dauer * trackBar2.Maximum);
            label1.Text = $"Startzeit: {getTimeCode(trackBar1.Value)}";
            label2.Text = $"Dauer: {getTimeCode(trackBar2.Value)}";
            setMarker();
        }
        private void trackBar1_Scroll(object sender, EventArgs e)
        {
            label1.Text = $"Startzeit: {getTimeCode(trackBar1.Value)}";
            setMarker();
        }
        void setMarker()
        {
            f.setStartStopTime(startzeit / (double)gesdauer, dauer / (double)gesdauer);
        }
        public string getTimeCode(int seconds)
        {
            var stunden = (int)Math.Floor(seconds / 3600.0);
            var minuten = (int)Math.Floor(seconds / 60.0) % 60;
            var sekunden = seconds % 60;
            var ergebnis = $"{stunden:D2}:{minuten:D2}:{sekunden:D2}";
            return ergebnis;

        }

        private void trackBar2_Scroll(object sender, EventArgs e)
        {
            label2.Text = $"Dauer: {getTimeCode(trackBar2.Value)}";
            f.setStartStopTime (startzeit/(double)gesdauer,dauer/(double)gesdauer);
        }

        private void button1_Click(object sender, EventArgs e)
        {
            if (gesdauer>=startzeit+ dauer)
            {
                gueltig = true;
                Close();
            }
            else
            {
                MessageBox.Show("Ungültige Werte");
            }
            
        }

        private void Zeitfinder_Load(object sender, EventArgs e)
        {
            Icon = Properties.Resources.Vorrennung_icon;
        }

        private void button2_Click(object sender, EventArgs e)
        {
            Close();
        }

        private void Zeitfinder_FormClosing(object sender, FormClosingEventArgs e)
        {
            fertig = true;
        }
    }
}
