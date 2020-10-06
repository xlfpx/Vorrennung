#define SOLA_SEHR_GENAU // für hq
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Vorrennung
{
    class VideoBeschleuniger
    {
        #region datentypen
        public class IOSampleBeziehung
        {
            public int inputsamplezahl;
            public int outputsamplezahl;
        }
        #endregion
        
        #region variablen
        bool locked;
        public bool aktuell;
        WavReader input;
        WavWriter output;
        long speedBlockSize;
        public List<double> beschleunigungsFaktoren;
        public int dauer => (int)(input.Count / input.samplingrate);
        public event progressInformation teilFortschrittChanged;
        public event progressInformation gesammtFortschrittChanged;
        double samplingrate;
        public List<string> tempDateiNamen;
        List<IOSampleBeziehung> InputVersusOutput;
        public string tempDateiOrdner;
        public string ffmpegPfad;
        int volumeBlockSize;
        public List<double> lautstaerken;
        public int debuglevel;
        public string ffprobePfad;
        StreamWriter errorlog;
        public DateTime startzeit = DateTime.Now;
        int blockzusammenfassung = 2;
        public speedupparams beschleunigungsParameter=new speedupparams ();
        public bool useSola;
        protected string inputdateiname;
        protected string outputdateiname;
        protected string tempwavdatei;

        public bool grundBeschleunigungViaFFMPEG ;
        public int solablockdiv;
        public int solasuchberdiv;
        public int solasuchschrdiv ;
        public bool dynaudnorm = true;


        public string AdditionalFFmpegAudioParams
        {
            get => Properties.Settings.Default.ExtraFFmpegParams;
            set => Properties.Settings.Default.ExtraFFmpegParams = value;
        }
        #endregion
        public delegate void progressInformation(object sender,double value);
        
        #region hilfsfunktionen
        void setzeGesammtFortschritt(int wert)
        {
            setzeGesammtFortschritt(wert / 100.0);
        }
        void setzeGesammtFortschritt(double wert)
        {
            gesammtFortschrittChanged?.Invoke(this, wert);
        }
        void setzeTeilFortschritt(int wert)
        {
            setzeTeilFortschritt(wert / 100.0);
        }
        void setzeTeilFortschritt(double wert)
        {
            teilFortschrittChanged?.Invoke(this, wert);
        }
        public System.Diagnostics.Process FfmpegCall(string param, bool start = true)
        {
            TraceExclamation("ffmpegcall");

            var p = new System.Diagnostics.Process();
            /*if (!usecmd)
            {*/
            p.StartInfo.UseShellExecute = true;
            p.StartInfo.WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden;

            if (debuglevel == 4) //für erweiterte debugzwecke
            {
                p.StartInfo.UseShellExecute = false;
                p.StartInfo.RedirectStandardError = true;
                p.StartInfo.RedirectStandardOutput = true;
                p.StartInfo.RedirectStandardInput = true;
                p.StartInfo.CreateNoWindow = true;

                //p.StartInfo.CreateNoWindow = true;
            }
            p.StartInfo.FileName = ffmpegPfad ;

            p.StartInfo.Arguments = param;//.Replace("\\", "/");
            TraceExclamation($"FFMPEG: {param}");
            if (start) { p.Start(); }
            return p;
        }
        public void ExtendedWaitForExit(System.Diagnostics.Process p, bool dynamic = true)
        {
            TraceExclamation("exwaitforexit");
            var ergebnis =
                $"\nCall: {p.StartInfo.FileName} {p.StartInfo.Arguments}\n/////////////////////////////////////////////////\n";
            if (!dynamic)
            {
                p.WaitForExit();
                //return;
                
                try
                {
                    if (p.StartInfo.RedirectStandardOutput)
                    {
                        var stdoutput = "\nStdoutput:---------------------------------------\n";
                        while (!p.StandardOutput.EndOfStream) { stdoutput =
                            $"{stdoutput}{p.StandardOutput.ReadLine()}\n"; }
                    }
                    if (p.StartInfo.RedirectStandardError)
                    {
                        var stderror = "\nStderror:-----------------------------------------\n";
                        while (!p.StandardError.EndOfStream) { stderror = $"{stderror}{p.StandardError.ReadLine()}\n"; }
                        stderror += "\n";
                        ergebnis = ergebnis + stderror;
                    }
                }
                catch (Exception e)
                {
                    MessageBox.Show(e.ToString());
                }
                ergebnis += "////////////////////////////////////////////////////\n";
                TraceExclamation(ergebnis, 4);
            }
            else
            {
                TraceExclamation(ergebnis, 4);
                while (!p.HasExited)
                {
                    p.WaitForExit(100);

                    if (p.StartInfo.RedirectStandardOutput)
                    {
                        while (!p.StandardOutput.EndOfStream)
                        {
                            var zeile = $"STDOUT: {p.StandardOutput.ReadLine()}\n";
                            System.Diagnostics.Trace.WriteLine(zeile);
                            TraceExclamation(zeile, 4);

                        }
                    }
                    if (p.StartInfo.RedirectStandardError)
                    {
                        while (!p.StandardError.EndOfStream)
                        {
                            var zeile = $"STDERR: {p.StandardError.ReadLine()}\n";
                            System.Diagnostics.Trace.WriteLine(zeile);
                            TraceExclamation(zeile, 4);
                        }
                    }
                }
                TraceExclamation("////////////////////////////////////////////////////\n", 4);
            }
        }
        public string GetInputFileName()
        {
            return inputdateiname;
        }
        public static string GetTimeCode(int seconds)
        {
            var stunden = (int)Math.Floor(seconds / 3600.0);
            var minuten = (int)Math.Floor(seconds / 60.0) % 60;
            var sekunden = seconds % 60;
            return $"{stunden:D2}:{minuten:D2}:{sekunden:D2}";
        }


        protected string generateTempFolder(string startpath)
        {
            var start = $"{startpath}{Path.DirectorySeparatorChar}Vorrennungtemp-";
            var ergebnis = start;
            var gefunden = false;
            for (var i = 0; i < 1000; i++)
            {
                ergebnis = start + i;
                if (Directory.Exists(ergebnis))
                    continue;
                
                Directory.CreateDirectory(ergebnis);
                gefunden = true;
                break;
            }
            if (!gefunden) { throw new Exception("Es konnte kein Tempordner angelegt werden."); }
            registerTempFile(ergebnis);
            return ergebnis;

        }
        public void deleteTempFiles()
        {
            foreach (var tempFileName in tempDateiNamen)
            {
                try
                {
                    if (tempFileName == "")
                        continue;
                    
                    if (File.Exists(tempFileName))
                        File.Delete(tempFileName);

                    if (!Directory.Exists(tempFileName))
                        continue;
                    
                    foreach (var f in Directory.EnumerateFiles(tempFileName))
                    {
                        try
                        {
                            File.Delete(f);
                        }
                        catch (Exception e)
                        {
                            TraceExclamation(e);
                        }
                    }
                    Directory.Delete(tempFileName);
                }
                catch (Exception e)
                {
                    TraceExclamation($"Datei \"{tempFileName}\" konnte nicht gelöscht werden. Details: \n{e}");
                }
            }
        }
        #region traces
        public void TraceExclamation(string wert)
        {

            switch (debuglevel)
            {
                case 0:
                    break;
                case 1:
                    System.Diagnostics.Trace.WriteLine(wert);
                    break;
                case 2:
                    Console.WriteLine(wert);
                    break;
                case 3:
                    MessageBox.Show(wert);
                    break;
                case 4:
                    if (errorlog == null)
                    {
                        var name = $"errorlog {startzeit.ToString().Replace(":", "-")}";
                        while (File.Exists(name))
                        {
                            name += "-";
                        }
                        name += ".txt";
                        errorlog = new StreamWriter(File.Open(name, FileMode.Create, FileAccess.Write, FileShare.None));
                    }
                    errorlog.WriteLine($"{DateTime.Now.Subtract(startzeit)}->:\n");
                    errorlog.WriteLine(wert);
                    errorlog.Flush();
                    break;
            }
        }
        public void TraceExclamation(object wert)
        {
            TraceExclamation(wert != null ? wert.ToString() : "null");
        }
        public void TraceExclamation(double wert)
        {
            TraceExclamation(wert.ToString());
        }
        public void TraceExclamation(decimal wert)
        {
            TraceExclamation(wert.ToString());
        }
        public void TraceExclamation(int wert)
        {
            TraceExclamation(wert.ToString());
        }
        public void TraceExclamation(object wert, int tracelevelRequired)
        {
            if (debuglevel == tracelevelRequired)
            {
                if (wert != null) { TraceExclamation(wert.ToString()); } else { TraceExclamation("null"); }
            }
        }
        #endregion
        public string createTempFileName(string dateiname)
        {
            TraceExclamation("Gentempfile");
            if (string.IsNullOrEmpty(tempDateiOrdner)||!Directory.Exists(tempDateiOrdner))
            {
                tempDateiOrdner = "";
                tempDateiOrdner=generateTempFolder(Directory.GetCurrentDirectory());
            }
            var ergebnis = tempDateiOrdner + Path.DirectorySeparatorChar ;
            var basis = ergebnis;
            var n=0;
            ergebnis = basis + dateiname;
            if (!File.Exists(ergebnis))
                return ergebnis;
            do
            {
                ergebnis = basis + n + dateiname;
                n++;
            } while (File.Exists(ergebnis) && n < 100000);
            return ergebnis;
        }
        public void registerTempFile(string tempfile)
        {
            TraceExclamation($"regtempfile: {tempfile}");
            if (tempDateiNamen == null) { tempDateiNamen = new List<string>(); }
            tempDateiNamen.Add(tempfile);
        }
#endregion

        public delegate void faktorenChanged(object sender, List<double> parameter);
        public delegate void beschleunigungsFaktorenChanged(object sender, List<double> parameter,double spielzeit,double zeitunterteilung,double samplingrate);
        public event faktorenChanged lautstaerkeVeraendert;
        public event beschleunigungsFaktorenChanged beschleunigungVeraendert;

        protected void generiereWavInputDatei()
        {
            
                TraceExclamation("genwavinput");
                var tmpaudioin=createTempFileName("tmpaudioin.wav");
                registerTempFile(tmpaudioin);
                /*if (dynaudnorm)//deprecated
                {
                    extendedWaitForExit(ffmpegCall("-v quiet -y -i \"" + inputdateiname + "\" -acodec pcm_s16le -ac 1 -af dynaudnorm=f=100:g=5  "+AdditionalFFmpegAudioParams+" \"" + tmpaudioin + "\""));
                }
                else { */
                    ExtendedWaitForExit(FfmpegCall(
                        $"-v quiet -y -i \"{inputdateiname}\" -acodec pcm_s16le -ac 1 {AdditionalFFmpegAudioParams} \"{tmpaudioin}\""));
                //}
                //input = new WavReader(File.Open(tmpaudioin, FileMode.Open, FileAccess.Read, FileShare.Read),0);
                input = new WavReader(new FileStream(tmpaudioin, FileMode.Open, FileAccess.Read, FileShare.None, 16777216),0);
            
                samplingrate = input.samplingrate;
                //speedBlockSize = input.samplingrate /20;//}-------------------------------------------------------------------------------
                //volumeBlockSize = (int)(input.samplingrate / 40);
                
                ///TODO
                // hier in zukunft werte via primzahlzerlegung generieren, sonst wirds ungenau
                speedBlockSize = input.samplingrate / 200;
                volumeBlockSize = (int)(input.samplingrate / 400);
            
                blockzusammenfassung = 2;
                Console.WriteLine($"Bevor Anpassung: {speedBlockSize} {blockzusammenfassung} {volumeBlockSize}");
                if (volumeBlockSize * blockzusammenfassung != speedBlockSize) { speedBlockSize = blockzusammenfassung * volumeBlockSize; }
                Console.WriteLine($"Nach Anpassung: {speedBlockSize} {blockzusammenfassung} {volumeBlockSize}");
        }

        #region externa
        public void gestammeltesSchweigen()
        {
            setzeGesammtFortschritt(0);
            if (!aktuell) { refresh(); }
            try
            {
                //List<double> schweigensamples=new List<double>();
                var tempaudiooutfilename = createTempFileName("schweigen");
                registerTempFile(tempaudiooutfilename);
                var tempaudioout = new WavWriter(new FileStream(tempaudiooutfilename, FileMode.Create, FileAccess.Write, FileShare.None, 16777216), 0, input.samplingrate);
                var beschfaktoren = new List<double>();
                var toleranz = 1.1;
                var sampleposition = 0;
                for (var i = 0; i < beschleunigungsFaktoren.Count(); i++)
                {
                    if (beschleunigungsFaktoren[i] >= toleranz * beschleunigungsParameter.minspeed)
                    {
                        beschfaktoren.Add(beschleunigungsFaktoren[i]);
                        for (var n = 0; n < speedBlockSize; n++)
                            if (sampleposition + n < input.Count) 
                                tempaudioout.write(input[sampleposition + n]);
                    }
                    sampleposition +=(int) speedBlockSize;
                }
                tempaudioout.close(true);
                var tmpaudioin = new WavReader(new FileStream(tempaudiooutfilename, FileMode.Open , FileAccess.Read, FileShare.None, 16777216), 0);
                setzeGesammtFortschritt(25);
                fastenViaSola(tmpaudioin,beschfaktoren);
                setzeGesammtFortschritt(100);
                tmpaudioin.close();
                spieleAb(tempwavdatei);
                
            }
            catch (Exception e)
            {
                TraceExclamation(e);
            }
        }
        public void loadParamsFromProperties()
        {
            TraceExclamation("proptoparams");
            var p = Properties.Settings.Default;
            var b = beschleunigungsParameter;
            aktuell = false;


            useSola = p.SolaVerwenden ;
            b.ableitungsglaettung = p.Ableitungsglaettung ;
            b.maxableitung = p.MaximalerAbfall ;
            b.useableitung = p.AbleitungBerueck ;
            b.minableitung = p.MinimalerAbfall;
            debuglevel = 1;
            
            ffmpegPfad = p.FFmpeg; ;
            ffprobePfad = p.FFprobe;
            b.intensity = p.Intensitaet;
            b.laut = p.Laut;
            b.leise = p.Leise;
            b.maxspeed = p.MaximaleBeschleunigung;
            b.minspeed = p.MinimaleBeschleunigung;
            b.minpausenspeed = p.MinimalePausenBeschleunigung;
            b.rueckpruefung = p.Rueckwaertspruefung;
            b.eigeneFps = p.EigeneFPS;
            b.fps = p.Fpswert;
            dynaudnorm = p.Dynaudnorm;


            solasuchberdiv = p.SolaBer;
            solablockdiv = p.SolaDiv;
            solasuchschrdiv = p.SolaSchritt;
            grundBeschleunigungViaFFMPEG=p.UseFFmpegToo;
        }
        public void setParamsToProperties()
        {
            TraceExclamation("paramstoprop");
            var p = Properties.Settings.Default;
            var b = beschleunigungsParameter;
            aktuell = false;


            p.SolaVerwenden = useSola;
            p.Ableitungsglaettung=b.ableitungsglaettung;
            p.MaximalerAbfall=b.maxableitung  ;
            p.AbleitungBerueck=b.useableitung;
            p.MinimalerAbfall = b.minableitung;
            debuglevel = 1;
                       
            p.FFmpeg = ffmpegPfad;
            p.FFprobe = ffprobePfad;
            p.Intensitaet = b.intensity;            
            p.Laut=b.laut ;
            p.Leise=b.leise;
            p.MaximaleBeschleunigung = b.maxspeed;
            p.MinimaleBeschleunigung = b.minspeed;
            p.MinimalePausenBeschleunigung = b.minpausenspeed;
            p.Rueckwaertspruefung=b.rueckpruefung ;
            p.Fpswert = b.fps;
            p.EigeneFPS = b.eigeneFps;
            p.Dynaudnorm = dynaudnorm;
            Console.WriteLine($"Dynaudnorm: {dynaudnorm}");
            p.SolaBer=solasuchberdiv ;
            p.SolaDiv=solablockdiv;
            p.SolaSchritt=solasuchschrdiv ;
            p.UseFFmpegToo = grundBeschleunigungViaFFMPEG;
        }

        /// <summary>
        /// Setzt die Eingabedatei auf die angegebene Datei. Dies löscht alle temporären Dateien und schließt sämtliche offenen Dateien.
        /// </summary>
        /// <param name="dateiname"></param>
        public void setInputFileName(string dateiname,bool useLast=false)
        {
            locked = useLast;
            TraceExclamation("setinputfilename");
            try{

        
                setzeGesammtFortschritt(0);
                inputdateiname = dateiname;
                if (!locked)
                {
                    clean();
                    
                    generiereWavInputDatei();
                }
                
                setzeGesammtFortschritt(25);
                //input = new WavReader(File.Open(dateiname, FileMode.Open, FileAccess.Read, FileShare.Read),0);
            
                generiereLautstaerken();
                setzeGesammtFortschritt(50);
                refresh();
                setzeGesammtFortschritt(100);
             
            }
            catch (Exception e)
            {
                TraceExclamation(e);
                MessageBox.Show($"Beim Festlegen der Eingabedatei ist ein Fehler aufgetreten: {e}");
            }
        }
        bool arbeitend;
        bool pending;
        public void refresh(){
           // TraceExclamation("refresh");
            lock (this)
            {
                if (arbeitend) { pending = true; aktuell = false; }
                if (inputdateiname != null && inputdateiname != "")
                {
                    arbeitend = true;
                    speedfaktoren(primitivespeedup);
                    if (!pending) { aktuell = true; }
                    pending = false;
                    arbeitend = false;
                }
            }
            
        }
        int nr;
        public void setOutputFileName(string dateiname)
        {
            TraceExclamation("setoutputfilename");
            try{
                if (output != null) { output = null; }
                outputdateiname = dateiname;
                tempwavdatei = createTempFileName($"tmpwavout{nr}.wav");
                nr++;
                registerTempFile(tempwavdatei);
               
                //output = new WavWriter(File.Open(tempwavdatei, FileMode.Create), 0, input.samplingrate);
                output = new WavWriter(new FileStream(tempwavdatei, FileMode.Create, FileAccess.Write, FileShare.None, 16777216), 0, input.samplingrate);

            }
            catch (Exception e)
            {
                TraceExclamation(e);
                MessageBox.Show($"Beim Festlegen der Ausgabedatei ist ein Fehler aufgetreten: {e}");
            }
        }
        
        public void beschleunige()
        {
            TraceExclamation("beschleunige");
            setzeGesammtFortschritt(0);
            if (!aktuell) { refresh(); }
            try
            {
                setzeGesammtFortschritt(25);
                
                    fastenViaSola();
               
                
                
                setzeGesammtFortschritt(50);
                createFastendVideo(inputdateiname, outputdateiname, tempwavdatei);
                setzeGesammtFortschritt(100);
            }
            catch (Exception e)
            {
                TraceExclamation(e);
                MessageBox.Show($"Beim Beschleunigen ist ein Fehler aufgetreten: {e}");
            }
        }

        public void beschleunigeAudio()
        {
            TraceExclamation("beschleunige");
            setzeGesammtFortschritt(0);
            if (!aktuell) { refresh(); }
            try
            {
                setzeGesammtFortschritt(25);
                
                    fastenViaSola();
               

                setzeGesammtFortschritt(70);
                //createFastendVideo(inputdateiname, outputdateiname, tempwavdatei);
                
                FfmpegCall(
                    $"-y -v quiet -i {FileFinder.toFileName(tempwavdatei, true)} -ab 128k {FileFinder.toFileName(outputdateiname, true)}");
                setzeGesammtFortschritt(100);
            }
            catch (Exception e)
            {
                TraceExclamation(e);
                MessageBox.Show($"Beim Beschleunigen ist ein Fehler aufgetreten: {e}");
            }
        }

        public void reinhoeren(int startzeit, int dauer)
        {
            TraceExclamation("beschleunige");
            setzeGesammtFortschritt(0);
            if (!aktuell) { refresh(); }
            try
            {
                setzeGesammtFortschritt(50);
                fastenViaSola(startzeit,dauer);
                


                
                //createFastendVideo(inputdateiname, outputdateiname, tempwavdatei);
               // ffmpegCall("-y -i " + FileFinder.toFileName(tempwavdatei, true) + " -ab 128k " + FileFinder.toFileName(outputdateiname + ".mp3", true));
                setzeGesammtFortschritt(100);
                spieleAb(tempwavdatei);
            }
            catch (Exception e)
            {
                TraceExclamation(e);
                MessageBox.Show($"Beim Beschleunigen ist ein Fehler aufgetreten: {e}");
            }
        }
        private void spieleAb(string tempwavout)
        {

            var p = new System.Diagnostics.Process();

            p.StartInfo.FileName = tempwavout;
            TraceExclamation($"Spiele: {tempwavout}");
            p.Start();
            System.Threading.Thread.Sleep(1500);
            var beende = false;
            while (!beende)
            {
                try
                {
                    var t = File.Open(tempwavout, FileMode.Append, FileAccess.Write, FileShare.None);
                    t.Close();
                    beende = true;
                }
                catch (Exception e)
                {
                    TraceExclamation($"Warte auf schließen des Programmes.... Exception: {e}");
                }
                System.Threading.Thread.Sleep(500);
            }


        }


        
        public void clean()
        {
            TraceExclamation("clean");
            input?.close();
            output?.close(true);
            if (tempDateiNamen != null) { deleteTempFiles(); }
        }
        #endregion
        /*

#if SOLA_HD
        int solablockdiv = 20;
        int solasuchberdiv = 60;
        int solasuchschrdiv = 1600;
#elif SOLA_HD_GENAU
        int solablockdiv = 20;
        int solasuchberdiv = 160;
        int solasuchschrdiv = 1600;
#elif SOLA_SD_GENAU
        int solablockdiv = 60;
        int solasuchberdiv = 160;
        int solasuchschrdiv = 1600;
#elif SOLA_SEHR_GENAU

        int solablockdiv = 120;
        int solasuchberdiv = 320;
        int solasuchschrdiv = 2400;
#else
        int solablockdiv = 40;//sd
        int solasuchberdiv = 160; //sd
        int solasuchschrdiv = 800; //sd
#endif
        */
        int solaueberdiv = 100;
        void fastenViaSola()
        {
            TraceExclamation("sola start");
            InputVersusOutput = new List<IOSampleBeziehung>();
            SpeedUp.solaKontinuierlich(input, (int)(input.samplingrate / solablockdiv), (int)(input.samplingrate / solasuchberdiv), (int)(input.samplingrate / solasuchberdiv), (int)(input.samplingrate / solasuchschrdiv), beschleunigungsFaktoren, (int)speedBlockSize, InputVersusOutput, solastatus,output);
            /*double[] werte = SpeedUp.solaKontinuierlich(input, (int)(input.samplingrate /solablockdiv), (int)(input.samplingrate /solasuchberdiv ),(int)( input.samplingrate /solasuchberdiv ),(int)( input.samplingrate / solasuchschrdiv), beschleunigungsFaktoren, (int)speedBlockSize ,InputVersusOutput,solastatus );
            TraceExclamation("sola write");

            setzeTeilFortschritt(.75);
            for (int i = 0; i < werte.Length; i++)
            {
                output.write(werte[i]);
                if ((i & 255) == 0)
                {
                    setzeTeilFortschritt(.75+.25*i/werte.Length );;
                }
            }*/
            output.close(true);
            setzeTeilFortschritt(1.0);
            TraceExclamation("Sola done");
        }
        
        void fastenViaSola(int startzeit,int dauer)
        {
            TraceExclamation("sola start");
            InputVersusOutput = new List<IOSampleBeziehung>();

            var tempaudiooutfilename = createTempFileName("schweigen");
            registerTempFile(tempaudiooutfilename);
            
            //WavWriter tempaudioout = new WavWriter(File.Open(tempaudiooutfilename, FileMode.Create, FileAccess.Write), 0, input.samplingrate);
            var tempaudioout = new WavWriter(new FileStream(tempaudiooutfilename, FileMode.Create, FileAccess.Write, FileShare.None, 16777216), 0, input.samplingrate);   


            var beschleunigungstmp=new List<double>();
            for (var i=(int)(startzeit *samplingrate) ;i<(startzeit+dauer)*samplingrate;i++){

                if (i < input.Count) { tempaudioout.write(input[i]); }
                if (i % speedBlockSize == 0) { beschleunigungstmp.Add(beschleunigungsFaktoren[i /(int) speedBlockSize]); }
            }
            tempaudioout.close(true);
            //WavReader daten = new WavReader(File.Open(tempaudiooutfilename, FileMode.Open, FileAccess.Read, FileShare.Read), 0);
            var daten = new WavReader(new FileStream(tempaudiooutfilename, FileMode.Open, FileAccess.Read, FileShare.Read, 16777216), 0);
            fastenViaSola(daten, beschleunigungstmp);
            daten.close();
            /*double[] werte = SpeedUp.solaKontinuierlich(daten, (int)(input.samplingrate / solablockdiv), (int)(input.samplingrate / solasuchberdiv), (int)(input.samplingrate / solasuchberdiv), (int)(input.samplingrate / solasuchschrdiv), beschleunigungstmp, (int)speedBlockSize, InputVersusOutput, solastatus);
            
            TraceExclamation("sola write");

            setzeTeilFortschritt(.75);
            for (int i = 0; i < werte.Length; i++)
            {
                output.write(werte[i]);
                if ((i & 255) == 0)
                {
                    setzeTeilFortschritt(.75 + .25 * i / werte.Length); ;
                }
            }
            output.close(true);
            setzeTeilFortschritt(1.0);*/
            TraceExclamation("Sola done");
        }
        
        void fastenViaSola(WavReader daten,List<double> beschleunigung)
        {
            TraceExclamation("sola start");
            InputVersusOutput = new List<IOSampleBeziehung>();

            SpeedUp.solaKontinuierlich(daten, (int)(input.samplingrate / solablockdiv), (int)(input.samplingrate / solasuchberdiv), (int)(input.samplingrate / solasuchberdiv), (int)(input.samplingrate / solasuchschrdiv), beschleunigung, (int)speedBlockSize, InputVersusOutput, solastatus, output);
            /*
              double[] werte = SpeedUp.solaKontinuierlich(daten, (int)(input.samplingrate / solablockdiv), (int)(input.samplingrate / solasuchberdiv), (int)(input.samplingrate / solasuchberdiv), (int)(input.samplingrate / solasuchschrdiv), beschleunigung, (int)speedBlockSize, InputVersusOutput, solastatus);
              TraceExclamation("sola write");
            
            setzeTeilFortschritt(.75);
            for (int i = 0; i < werte.Length; i++)
            {
                output.write(werte[i]);
                if ((i & 255) == 0)
                {
                    setzeTeilFortschritt(.75 + .25 * i / werte.Length); ;
                }
            }*/
            output.close(true);
            setzeTeilFortschritt(1.0);
            TraceExclamation("Sola done");
        }

        void solastatus(object sender, double wert)
        {
            setzeTeilFortschritt(wert * 1);
        }
        
        #region portierteMethoden


        #region beschleunigungsfunktion
        public delegate double speedfakt(double wert, double wertv, double wertn,speedupparams parameter, ref object scratch);//wert= lautstärke aktuell, wertv= lautstärke davor, wertn= lautstärke danach, parameter=eigene parameter. -1 = nicht vorhanden
        public class speedupparams
        {
       
            public double maxspeed = 50;
            public double minspeed = 2;
            public double leise = .02;
            public double laut = .4;
            public double intensity = .1;
            public double minpausenspeed = 1;
            public double maxableitung=.2;
            public double minableitung = .1;
            public double ableitungsglaettung = .5;
            public bool useableitung = true;
            public bool rueckpruefung = true;
            public bool eigeneFps;
            public int fps = 1;
        }
        double primitivespeedup(double wert, double wertv, double wertn,speedupparams parameter, ref object scratch)
        {
            
            if (scratch == null) { scratch=new double[2] ;}
            var tempmemory = (double[])scratch;
            var leise = parameter.leise;

            var rueckgabe = tempmemory[0] ;
            var last = tempmemory[0];
            var lastableitung = tempmemory[1];
            var aktableitung = lastableitung * parameter.ableitungsglaettung + (wertn - wertv) * (1 - parameter.ableitungsglaettung);

            if (wertv > parameter.laut || wert > parameter.laut || wertn > parameter.laut) { rueckgabe = 0; }
            else
            {
                if (wert < leise&&(!(aktableitung>-parameter.maxableitung)||!(aktableitung<-parameter.minableitung  )||!parameter.useableitung ))
                {
                    rueckgabe = (last + 1 + parameter.intensity) * (last + 1) - 1;
                    if (rueckgabe + parameter.minspeed < parameter.minpausenspeed) { rueckgabe = parameter.minpausenspeed - parameter.minspeed; }
                }
                if (rueckgabe < 0) { rueckgabe = 0; }
                if (rueckgabe > parameter.maxspeed - parameter.minspeed) { rueckgabe = parameter.maxspeed - parameter.minspeed; }
            }

            tempmemory[0] = rueckgabe;
            tempmemory[1] = aktableitung;

            return rueckgabe + parameter.minspeed;
        }
#endregion

        class bearbeitungsfortschritt//nur damit man eine referenz zum locken hat
        {
            public int bearbeitet;
        }
        public void generiereLautstaerken( )
        {
            TraceExclamation("genVolumes");
            if (!locked)
            {
                lautstaerken = new List<double>();
                // double minimum = 0, maximum = 0, wertv = 0, ableitung = 0;
                //long index = 0;

                int n;
                var informer = (int)(input.Count / (volumeBlockSize * 50));
                double tmp;
                setzeTeilFortschritt(0);
                var threads = new Task[Environment.ProcessorCount];
                var wert = 0;

                var blockcount = (int)(input.Count / volumeBlockSize);
                var tempvolumes = new double[blockcount];

                var bearbeitet = new bearbeitungsfortschritt();
                GC.Collect();
                GC.WaitForPendingFinalizers();

                // PerformanceCounter ramCounter = new PerformanceCounter("Memory", "Available MBytes");
                var start = DateTime.Now;
                // Console.WriteLine("Free MEmory: " + ramCounter.NextValue());
                var reqMem = input.Count * (8 + input.header.blockalign) / 1048576 + 100;
                Console.WriteLine($"Required Memory: {reqMem}");
                Console.WriteLine($"Is64Bit process? {Environment.Is64BitProcess}");
                var canReadIntoMemory = false;
                //                if ((Environment.Is64BitProcess ? (ramCounter.NextValue()) : Math.Min(ramCounter.NextValue(), 2000)) > input.Count * (8 + input.header.blockalign) / 1048576 + 100) // wenn genug speicher da ist, gilt es ihn zu nutzen 8 = für double 
                double[] wertespeicher=null;
                var inputcount = (int)input.Count;
                try
                {
                    wertespeicher = input.readValues(inputcount);
                    canReadIntoMemory = true;
                }catch{
                    canReadIntoMemory = false;
                }
                if (canReadIntoMemory) // wenn genug speicher da ist, gilt es ihn zu nutzen 8 = für double 
                {
                    Console.WriteLine("par vol");
                    input.seekSample(0);
                    
                    System.Diagnostics.Trace.WriteLine("gelesen");
                    //wertespeicher = new double[inputcount];

                    Parallel.For(0, blockcount, nr =>
                    {
                        double minimum = 0, maximum = 0, ableitung = 0, wertv;
                        double tmps;
                        var volversatz = nr * volumeBlockSize;

                        double schnitt = 0;
                        var samplezahl = 0;
                        for (var i = 0; i < volumeBlockSize; i++)
                        {
                            if (i + volversatz < inputcount)
                            {
                                samplezahl++;
                                schnitt += wertespeicher[i + volversatz];
                            }
                            
                        }
                        schnitt /= samplezahl;
                        wertv = wertespeicher[volversatz]-schnitt;
                        for (var i = 0; i < volumeBlockSize; i++)
                        {
                            if (i + volversatz < inputcount)
                            {
                                tmps = wertespeicher[i + volversatz]-schnitt;
                                if (tmps < minimum) { minimum = tmps; }
                                if (tmps > maximum) { maximum = tmps; }
                                if (Math.Abs(tmps - wertv) > ableitung) { ableitung = Math.Abs(tmps - wertv); }
                            }
                        }

                        lock (bearbeitet)
                        {
                            bearbeitet.bearbeitet++;
                            if (bearbeitet.bearbeitet % informer == 0)
                            {
                                wert++;
                                if (wert <= 100) { setzeTeilFortschritt(wert); }
                            }
                        }
                        tempvolumes[nr] = (maximum - minimum + ableitung) / 4;//:4  da maximum-minimum und ableitung ein maximum von jeweils 2 haben
                    });
                    Console.WriteLine("parallellisiert");
                }
                else
                {
                    Console.WriteLine("seq vol");
                    //int inputcount = (int)input.Count;

                    for (var nr = 0; nr < blockcount; nr++)
                    {
                        double minimum = 0, maximum = 0, ableitung = 0, wertv;

                        var volversatz = nr * volumeBlockSize;


                        double schnitt = 0;
                        var samplezahl = 0;
                        for (var i = 0; i < volumeBlockSize; i++)
                        {
                            if (i + volversatz < inputcount)
                            {
                                samplezahl++;
                                schnitt += input[i + volversatz];
                            }

                        }
                        schnitt /= samplezahl;
                        wertv = input[volversatz] - schnitt;
                        // wertv = input[volversatz];
                        for (var i = 0; i < volumeBlockSize; i++)
                        {
                            if (i + volversatz >= inputcount)
                                continue;
                            
                            tmp = input[i + volversatz];
                            if (tmp < minimum) { minimum = tmp; }
                            if (tmp > maximum) { maximum = tmp; }
                            if (Math.Abs(tmp - wertv) > ableitung) { ableitung = Math.Abs(tmp - wertv); }
                        }

                        bearbeitet.bearbeitet++;
                        if (bearbeitet.bearbeitet % informer == 0)
                        {
                            wert++;
                            if (wert <= 100) { setzeTeilFortschritt(wert); }
                        }

                        tempvolumes[nr] = (maximum - minimum + ableitung) / 4; //:4  da maximum-minimum und ableitung ein maximum von jeweils 2 haben
                    }

                }
                Console.WriteLine($"Zeit: {DateTime.Now.Subtract(start).TotalMilliseconds / 1000}");
                foreach (var tempVolume in tempvolumes)
                    lautstaerken.Add(tempVolume);

                var loudest = percentalMaximum(.9, lautstaerken);
                for (n = 0; n < lautstaerken.Count; n++)
                {
                    lautstaerken[n] = Math.Min(lautstaerken[n] / loudest, 1);

                    if (n % (lautstaerken.Count / 50) != 0)
                        continue;
                    
                    wert ++;
                    if (wert <= 100)
                        setzeTeilFortschritt(wert);
                }
                setzeTeilFortschritt(100);


                lautstaerkeVeraendert?.Invoke(this, lautstaerken);
            }
            else
            {
                TraceExclamation("Locked.");
            }
        }

        public void speedfaktoren( speedfakt funktion)//zusammenfassung== wie viele blöcke als ein ganzes gesehen werden sollen
        {
            TraceExclamation("speedfakt");
            if (!locked)
            {
                beschleunigungsFaktoren = new List<double>();
                var tmp = new List<double>();
                double aktwert = 0, aktcount = 0;
                var altMinSpeed = beschleunigungsParameter.minspeed;
                var altMaxSpeed = beschleunigungsParameter.maxspeed;
                var altMinPausenSpeed = beschleunigungsParameter.minpausenspeed;
                if (grundBeschleunigungViaFFMPEG)
                {
                    beschleunigungsParameter.maxspeed /= altMinSpeed;
                    beschleunigungsParameter.minpausenspeed /= altMinSpeed;
                    beschleunigungsParameter.minspeed = 1;
                }

                for (var i = 0; i < lautstaerken.Count; i += blockzusammenfassung)
                {
                    aktwert = 0; aktcount = 0;
                    for (var n = 0; n < blockzusammenfassung; n++)
                    {
                        if (i + n < lautstaerken.Count)
                        {
                            aktwert += lautstaerken[i + n];
                            aktcount++;
                        }
                    }
                    aktwert /= aktcount;
                    //   TraceExclamation(aktwert);
                    tmp.Add(aktwert);

                    setzeTeilFortschritt(50 * i / lautstaerken.Count);
                }

                //TraceExclamation("--");
                double wertv = -1;
                var wert = tmp[0];
                var wertn = tmp[1];
                object speicher = null;
                for (var i = 0; i < tmp.Count; i++)
                {
                    wertn = i == tmp.Count - 1 ? -1 : tmp[i + 1];
                    
                    beschleunigungsFaktoren.Add(funktion.Invoke(wert, wertv, wertn, beschleunigungsParameter, ref  speicher));

                    wertv = wert;
                    wert = wertn;
                    setzeTeilFortschritt(50 + 25 * i / tmp.Count);

                }
                wertv = -1;
                wert = tmp[tmp.Count - 1];
                double funktionswert;
                speicher = null;
                if (beschleunigungsParameter.rueckpruefung)
                {
                    for (var i = tmp.Count - 1; i >= 0; i--)
                    {
                        wertn = i == 0 ? -1 : tmp[i - 1];
                        
                        funktionswert = funktion.Invoke(wert, wertv, wertn, beschleunigungsParameter, ref  speicher);
                        beschleunigungsFaktoren[i] = Math.Min(beschleunigungsFaktoren[i], funktionswert);

                        wertv = wert;
                        wert = wertn;
                        setzeTeilFortschritt(75 + 25 * (tmp.Count - 1 - i) / tmp.Count);

                    }
                }

                setzeTeilFortschritt(100);

                TraceExclamation($"{beschleunigungsFaktoren.Count} {lautstaerken.Count}");
                if (beschleunigungVeraendert != null)
                {
                    var spielzeit = beschleunigungsFaktoren.Sum(t => speedBlockSize / t) / samplingrate;
                    beschleunigungVeraendert.Invoke(this, beschleunigungsFaktoren, spielzeit, speedBlockSize, samplingrate);

                }
                //return beschleunigungsFaktoren;
                if (grundBeschleunigungViaFFMPEG)
                {
                    beschleunigungsParameter.maxspeed = altMaxSpeed;
                    beschleunigungsParameter.minpausenspeed = altMinPausenSpeed;
                    beschleunigungsParameter.minspeed = altMinSpeed;
                }
                System.Diagnostics.Trace.WriteLine("Fertig");
            }
            else
            {
                TraceExclamation("locked");
            }
        }
        
        public void createFastendVideo(string inputvideo, string outputvideo, string audiodatei)
        {
            var startzeit = DateTime.Now;
            setzeTeilFortschritt(0);
           
            System.Diagnostics.Process writeprocess, readprocess,infoprocess;
            infoprocess = new System.Diagnostics.Process();
            writeprocess = new System.Diagnostics.Process();
            readprocess = new System.Diagnostics.Process {StartInfo = {UseShellExecute = false}};

            writeprocess.StartInfo.UseShellExecute = false;
            infoprocess.StartInfo.UseShellExecute = false;

            readprocess.StartInfo.RedirectStandardInput = true;
            readprocess.StartInfo.RedirectStandardOutput = true;
            readprocess.StartInfo.RedirectStandardError = true;
            readprocess.StartInfo.CreateNoWindow = true;
            
            writeprocess.StartInfo.RedirectStandardInput = true;
            writeprocess.StartInfo.RedirectStandardOutput = true;
            writeprocess.StartInfo.RedirectStandardError = true;
            writeprocess.StartInfo.CreateNoWindow =true;

            infoprocess.StartInfo.CreateNoWindow = true;
            infoprocess.StartInfo.RedirectStandardError = true;
            infoprocess.StartInfo.RedirectStandardOutput = true;

            writeprocess.StartInfo.FileName = ffmpegPfad;
            readprocess.StartInfo.FileName = ffmpegPfad;
            infoprocess.StartInfo.FileName = ffprobePfad;

            infoprocess.StartInfo.Arguments =
                $"-v error -select_streams v:0 -show_entries stream=width,height,avg_frame_rate,bit_rate -of default=noprint_wrappers=1 {FileFinder.toFileName(inputvideo, true)}";
            infoprocess.Start();
            infoprocess.WaitForExit();
            var daten = infoprocess.StandardOutput.ReadToEnd();
            var tmpstring = "";
            int width = 0, height = 0;
            double framerate = 1;
            var framerateString = "1/1";
            var bitrate = "64k";
            for (var i = 0; i < daten.Split('\n').Count (); i++)
            {
                tmpstring = daten.Split('\n')[i].Trim ();
                if (tmpstring.StartsWith("height="))
                    height = int.Parse( tmpstring.Substring("height=".Count()));
                else if (tmpstring.StartsWith("width="))
                    width = int.Parse(tmpstring.Substring("width=".Count()));
                else if (tmpstring.StartsWith("avg_frame_rate=")){
                    var t=tmpstring.Substring("avg_frame_rate=".Count ());
                    var t1=t.Split('/')[0];
                    var t2=t.Split('/')[1];
                    System.Diagnostics.Trace.WriteLine($"{t} {t1} {t2}");
                    framerateString = t;
                    framerate=double.Parse(t1)/double.Parse(t2);
                    System.Diagnostics.Trace.WriteLine (
                        $"Framerate: {framerate} t1: {t1} t2: {t2} ges: {tmpstring} splitted: {t}");
                }
                else if (tmpstring.StartsWith("bit_rate="))
                {
                    bitrate = tmpstring.Substring("bit_rate=".Count());
                    System.Diagnostics.Trace.WriteLine($"Bitrate: {bitrate}");
                }
            }
            if (width<=0 || height <= 0) { throw new Exception("Kein Video gefunden."); }
            if (beschleunigungsParameter.eigeneFps)
            {
                framerate = beschleunigungsParameter.fps;
                framerateString = $"{framerate.ToString().Trim()}/1";
            }

            var samples = InputVersusOutput.Aggregate<IOSampleBeziehung, long>(0, 
                (current, t) => current + t.inputsamplezahl);

            var gesammtzeit = samples / samplingrate;
            var gesammtframes = (int)Math.Floor(samples * framerate / samplingrate);

            var k = samplingrate / framerate;// alle k samples muss 1 frame eingesetzt werden

            var indizes = new List<int>();

            long realsamples = 0;
            double realzeit = 0;
            double lastzeit = 0;
            TraceExclamation($"{gesammtframes} {realzeit} {gesammtzeit}");

            long aktoutputsample = 0;
            double aktinputsample = 0;
            //experimentell anders berechnet
            /*
            for (int i = 0; i < InputVersusOutput.Count; i++)
            {
                double faktor = InputVersusOutput[i].inputsamplezahl;
                if (InputVersusOutput[i].outputsamplezahl == 0)
                {
                }
                else
                {
                    faktor /= (double)InputVersusOutput[i].outputsamplezahl;
                    aktinputsample = realsamples;
                    for (int n = 0; n < InputVersusOutput[i].outputsamplezahl; n++)
                    {
                        if (aktoutputsample / (double)samplingrate - lastzeit >= 1.0 / framerate)
                        {
                            lastzeit = aktoutputsample / (double)samplingrate;
                            indizes.Add((int)Math.Floor(framerate * aktinputsample / samplingrate));
                        }
                        aktinputsample += faktor;
                        aktoutputsample++;
                    }
                }
                realsamples += InputVersusOutput[i].inputsamplezahl;
                setzeTeilFortschritt((int)(25 * i / InputVersusOutput.Count));
            }*/
            double currentTime = 0;
            double currentError = 0;
            for (var i = 0; i < samples; i++)
            {
                var tmpval=  beschleunigungsFaktoren[(int)(i / speedBlockSize)] ;
                currentTime = i / samplingrate;
                currentError += 1/tmpval / samplingrate;
                if (currentError > 1.0 / framerate)
                {
                    currentError -= 1.0 / framerate;
                    indizes.Add((int)Math.Floor(framerate * currentTime));

                }
                if (i % 44100 == 0)
                {
                    setzeTeilFortschritt((int)(i / (double)samples*25));
                }
            }
            
            /*String tempvideodatei = createTempFileName("tempvideo.avi");
            registerTempFile(tempvideodatei);*/

            readprocess.StartInfo.Arguments =
                $"-v quiet -i {FileFinder.toFileName(inputvideo, true)} -c:v rawvideo -pix_fmt yuv420p -f image2pipe -r {framerateString} pipe:1";
            //writeprocess.StartInfo.Arguments = "-y -v quiet -f rawvideo -vcodec rawvideo -s "+width.ToString().Trim()+"x"+height.ToString().Trim()+" -pix_fmt yuv420p -r 1 -i - -an " + FileFinder.toFileName(tempvideodatei , true);// "-c:v rawvideo -pix_fmt rgb24 -s 1024x768 -i pipe:0  -an " + @"D:\root\downloads\ffmpegoutput.mp4";
            writeprocess.StartInfo.Arguments =
                $"-y -v quiet -i {FileFinder.toFileName(audiodatei, true)} -f rawvideo -vcodec rawvideo -s {width.ToString().Trim()}x{height.ToString().Trim()} -pix_fmt yuv420p -r {framerateString} -i - -acodec mp3 -ab 128k -vb {bitrate} -crf 0 -q:v 0 {FileFinder.toFileName(outputvideo, true)}";// "-c:v rawvideo -pix_fmt rgb24 -s 1024x768 -i pipe:0  -an " + @"D:\root\downloads\ffmpegoutput.mp4";
            //-i \"" + audiodatei + "\"  -acodec mp3 -ab 128k -map 0:0 -map 1:0
            System.Diagnostics.Trace.WriteLine($"rparams: {readprocess.StartInfo.Arguments}");
            System.Diagnostics.Trace.WriteLine($"wparams: {writeprocess.StartInfo.Arguments}");
            readprocess.Start();
            writeprocess.Start();
            var inputbuffer = new byte[1<<24];
            var outputbuffer = new byte[inputbuffer.Length];
            int bytezahl;
            long aktbyte = 0;
            var aktframe = 0;
            var indexindex = 0;
            var zuschreiben = 0;
            var bildByteCount = width * height * 3 / 2;
            var aktbildbyte = 0;
            var inputstream = new BufferedStream(readprocess.StandardOutput.BaseStream,outputbuffer.Length );
            var inputreader = new BinaryReader(inputstream);
       
            var skipcount = 0;
            int verbleibend;
            int startindex;
            var moduler = indizes.Count / 100;
            Task<string> ereadtask, ewritetask;
            ereadtask = readprocess.StandardError.ReadToEndAsync();
            ewritetask = writeprocess.StandardError.ReadToEndAsync();
            int got;
            var maxread = 0;
            System.Diagnostics.Trace.WriteLine($"Indizes: {indizes.Count()}");
            while (!readprocess.HasExited||!readprocess.StandardOutput.EndOfStream)
            {
               // if (indexindex >= indizes.Count()) { break; }
               if (ereadtask.IsCanceled||ereadtask.IsFaulted||ereadtask.IsCompleted){ ereadtask=readprocess.StandardError.ReadToEndAsync();}
                
                do
                {
                    // System.Diagnostics.Trace.WriteLine("innerLoop " );
                    // System.Diagnostics.Trace.WriteLine("reading");
                    
                    bytezahl =inputreader.Read (inputbuffer, 0, inputbuffer.Count());
                    got = bytezahl;
                    if (got > maxread) { maxread = got; }
                    if (indexindex >= indizes.Count()) { 
                        skipcount += bytezahl;
                        try {
                            readprocess.Kill();
                        }
                        catch
                        {
                            // ignored
                        }

                        break; 
                    }
                    if (bytezahl > 0)
                    {
                        verbleibend = bytezahl;
                        startindex = 0;
                        while (verbleibend > 0)
                        {
                            if (indexindex >= indizes.Count) { 
                                skipcount += verbleibend;
                                for (var counter = 0; !readprocess.HasExited && counter < 30; counter ++)
                                {
                                    try
                                    {
                                        readprocess.Close();
                                    }
                                    catch (Exception e)
                                    {
                                        System.Diagnostics.Trace.WriteLine(e);
                                    }
                                    System.Threading.Thread.Sleep(500);
                                }
                                break; 
                            }
                            zuschreiben = 0;
                            var schreibbar = Math.Min(verbleibend, bildByteCount - aktbildbyte);

                            if (aktframe == indizes[indexindex])
                            {
                                zuschreiben = schreibbar;
                            }    
                            if (schreibbar == bildByteCount - aktbildbyte)
                            {
                                aktbildbyte = 0;
                                if (aktframe == indizes[indexindex]) { 
                                    indexindex++;
                                    if (indexindex % moduler == 0) { setzeTeilFortschritt(25 + indexindex * 75 / indizes.Count); }
                                }
                                aktframe++;
                            }
                            else
                            {
                                aktbildbyte += schreibbar;
                            }
                                      
                            if (zuschreiben > 0)
                            {
                                if (ewritetask.IsCanceled || ewritetask.IsFaulted || ewritetask.IsCompleted) { ewritetask = writeprocess.StandardError.ReadToEndAsync(); }
                                writeprocess.StandardInput.BaseStream.Write(inputbuffer, startindex, zuschreiben);
                            }
                      
                            verbleibend -= schreibbar;
                            startindex += schreibbar;
                        }
                    }
                } while (bytezahl > 0);
                System.Diagnostics.Trace.WriteLine($"Warte auf wunder {got} {skipcount}");
           
                System.Threading.Thread.Sleep(100);
            }
            System.Diagnostics.Trace.WriteLine($"Maxread: {maxread}");
            System.Diagnostics.Trace.WriteLine ("Schließe stream");
            writeprocess.StandardInput.BaseStream.Close();
            System.Diagnostics.Trace.WriteLine("Fertig gelesen");
            writeprocess.WaitForExit();
            System.Diagnostics.Trace.WriteLine("Fertig geschrieben");
            System.Diagnostics.Trace.WriteLine($"Zeitaufwand: {DateTime.Now.Subtract(startzeit).TotalSeconds}s");
           // extendedWaitForExit(ffmpegCall(" -y -i \"" + tempvideodatei + "\" -i \"" + audiodatei + "\"  -acodec mp3 -ab 128k -map 0:0 -map 1:0 \"" + outputvideo + "\""));
            System.Diagnostics.Trace.WriteLine($"Byteskip: {skipcount} in frames {skipcount / bildByteCount}");
            setzeTeilFortschritt(90);
            if (grundBeschleunigungViaFFMPEG){

            
                var grundBeschleunigung = beschleunigungsParameter.minspeed;
                var filterA = "atempo=1";
                var filterV = $"setpts={1 / grundBeschleunigung}*PTS";
            
                while (grundBeschleunigung >1)
                {
                    if (grundBeschleunigung > 2)
                    {
                        filterA += ",atempo=2.0";
                        grundBeschleunigung/=2;
                    }else{
                        filterA+= $",atempo={grundBeschleunigung}";
                        grundBeschleunigung=1;
                    }
                }
            }
            setzeTeilFortschritt(100);
        }
        
        public void createFastendVideoOld(string inputvideo, string outputvideo, string audiodatei)
        {
            TraceExclamation("fastendvid");
            setzeTeilFortschritt(0);
            var framerate = 1;
            var codesekunden = 600;
            var aktzeit = 0;

            var samples = InputVersusOutput.Aggregate<IOSampleBeziehung, long>(0, 
                (current, t) => current + t.inputsamplezahl);

            var gesammtzeit = samples / samplingrate;
            var gesammtframes = (int)Math.Floor((double)samples * framerate / samplingrate);
            double frameindex = 0;
            var k = samplingrate / framerate;// alle k samples muss 1 frame eingesetzt werden
            double aktk = 0;
            var indizes = new List<int>();
            TraceExclamation("");
            long realsamples = 0;
            double realzeit = 0;
            double lastzeit = 0;
            TraceExclamation($"{gesammtframes} {realzeit} {gesammtzeit}");
            
            long aktoutputsample = 0;
            double aktinputsample = 0;
            for (var i = 0; i < InputVersusOutput.Count; i++)
            {
                double faktor = InputVersusOutput[i].inputsamplezahl;
                if (InputVersusOutput[i].outputsamplezahl != 0)
                {
                    faktor /= InputVersusOutput[i].outputsamplezahl;
                    aktinputsample = realsamples;
                    for (var n = 0; n < InputVersusOutput[i].outputsamplezahl; n++)
                    {
                        if (aktoutputsample / samplingrate - lastzeit >= 1.0 / framerate)
                        {
                            lastzeit = aktoutputsample / samplingrate;
                            indizes.Add((int)Math.Floor(framerate * aktinputsample / samplingrate));
                        }
                        aktinputsample += faktor;
                        aktoutputsample++;
                    }
                }


                realsamples += InputVersusOutput[i].inputsamplezahl;
                setzeTeilFortschritt(50 * i / InputVersusOutput.Count); 
            }
            TraceExclamation("");
            var versatz = 1;
            var GenerierteBildzahl = framerate * codesekunden;
            var subtraktor = 0;
            var realindex = 0;
            var transindex = 0;
            var maxrealindex = framerate * codesekunden;
            var neuerzeugen = false;
            var first = true;
            var bilddateipraefix = createTempFileName("skipbild-");
            var mergedateipraefix = createTempFileName( "mergebild-");
            var dateiendung = ".jpg";
            var tempvideo1 = createTempFileName ("tmpsmall.mp4");
            var tempvideo2 = createTempFileName("tmpmedium.mp4");
            var tempvideo3 = createTempFileName("tmplarge.mp4");
            var concatfile =createTempFileName("list.txt");
            registerTempFile (tempvideo1);
            registerTempFile (tempvideo2);
            registerTempFile (tempvideo3);
            registerTempFile (concatfile);
            for (var i = 0; i < GenerierteBildzahl; i++)
                tempDateiNamen.Add(bilddateipraefix + (i + versatz) + dateiendung);
            
            for (var i = 0; i < maxrealindex; i++)
                tempDateiNamen.Add(mergedateipraefix + (i + versatz) + dateiendung);

            var tmpstream = new FileStream(concatfile, FileMode.Create, FileAccess.Write, FileShare.None, 16777216); // File.OpenWrite(concatfile);
            var tw = new StreamWriter(tmpstream);

            tw.WriteLine($"file '{tempvideo2}'");
            tw.WriteLine($"file '{tempvideo1}'");
            tw.Close();


            for (var i = 0; i < indizes.Count; i++)
            {
                transindex = indizes[i] - subtraktor;
                neuerzeugen = false;
                while (transindex >= GenerierteBildzahl)
                {
                    aktzeit += codesekunden;
                    subtraktor += GenerierteBildzahl;
                    transindex -= GenerierteBildzahl;

                    neuerzeugen = true;
                }
                if (first || neuerzeugen)
                {
                    first = false;
            
                    for (var n = 0; n < GenerierteBildzahl; n++)
                    {
                        var dateiname = bilddateipraefix + (n + versatz) + dateiendung;
                        if (File.Exists(dateiname))
                        {
                            File.Delete(dateiname);
                        }
                    }
                    TraceExclamation("FFmpeg....");
                    ExtendedWaitForExit(FfmpegCall(
                        $" -y -r {framerate} -i \"{inputvideo}\" -r {framerate} -ss {GetTimeCode(aktzeit)} -t {codesekunden} \"{bilddateipraefix}%d{dateiendung}\""));
                    
                    TraceExclamation("Returned.");
                }
                if (File.Exists(mergedateipraefix + (realindex + versatz) + dateiendung)) { File.Delete(mergedateipraefix + (realindex + versatz) + dateiendung); }
                try
                {
                    File.Move(bilddateipraefix + (transindex + versatz) + dateiendung, mergedateipraefix + (realindex + versatz) + dateiendung);
                    
                    realindex++;
                }
                catch (Exception e)
                {
                    TraceExclamation("------------------------------------------");
                    TraceExclamation(
                        $"Eine Datei konnte nicht verschoben werden. : {bilddateipraefix}{(transindex + versatz)}{dateiendung} -> {mergedateipraefix}{(realindex + versatz)}{dateiendung}");
                    TraceExclamation(e);
                }
                if (realindex >= maxrealindex)
                {
                    realindex = 0;
                    
                    ExtendedWaitForExit(FfmpegCall(
                        $" -y -framerate {framerate} -i \"{mergedateipraefix}%d{dateiendung}\" \"{tempvideo1}\""), true);
                    if (File.Exists(tempvideo2))
                    {
                        ExtendedWaitForExit(FfmpegCall(
                            $" -y -i \"{tempvideo2}\" -i \"{tempvideo1}\" -filter_complex \"[0:0] [1:0] concat=n=2:v=1:a=0 [v] \" -map [v] \"{tempvideo3}\""));
                    
                        File.Delete(tempvideo2);
                        File.Move(tempvideo3, tempvideo2);
                    }
                    else
                    {
                        File.Move(tempvideo1, tempvideo2);
                    }
                    

                    for (var n = 0; n < maxrealindex; n++)
                    {
                        var dateiname = mergedateipraefix + (n + versatz) + dateiendung;
                        if (File.Exists(dateiname))
                        {
                            File.Delete(dateiname);
                        }
                    }
                }
                setzeTeilFortschritt(50 + 50 * i / indizes.Count); 
            }

            if (realindex != 0)
            {
                ExtendedWaitForExit(FfmpegCall(
                    $" -y -framerate {framerate} -i \"{mergedateipraefix}%d{dateiendung}\" \"{tempvideo1}\""), true);
                if (File.Exists(tempvideo2))
                {
                  
                    ExtendedWaitForExit(FfmpegCall(
                        $" -y -i \"{tempvideo2}\" -i \"{tempvideo1}\" -filter_complex \"[0:0] [1:0] concat=n=2:v=1:a=0 [v] \" -map [v] \"{tempvideo3}\""));
                    File.Delete(tempvideo2);
                    File.Move(tempvideo3, tempvideo2);
                }
                else
                {
                    File.Move(tempvideo1, tempvideo2);
                }
            } 
            // extendedWaitForExit(ffmpegCall(" -y -i \"" + tempvideo2 + "\" -i \"" + audiodatei + "\" -vcodec copy -acodec mp3 -ab 128k -map 0:0 -map 1:0 \"" + outputvideo + "\""));
            ExtendedWaitForExit(FfmpegCall(
                $" -y -i \"{tempvideo2}\" -i \"{audiodatei}\"  -acodec mp3 -ab 128k -map 0:0 -map 1:0 \"{outputvideo}\""));
            setzeTeilFortschritt(100);
        }
        #endregion
        
        public static double percentalMaximum(double prozentsatz, List<double> werte)
        {
            System.Diagnostics.Trace.WriteLine("Permax");
            var klon = new List<double>(werte.ToArray()); //arbeitskopie
            klon.Sort();
            return klon[(int)(klon.Count() * prozentsatz)];
            /*while (klon.Count > werte.Count * prozentsatz)
            {
                klon.Remove(klon.Max());
            }
            return klon.Max();*/
        }
    }
}
