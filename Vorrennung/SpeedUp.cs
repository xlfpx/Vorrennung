using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Collections.Concurrent;
namespace Vorrennung
{
    class SpeedUp
    {
        protected static double ReadQuickly(int index,WavReader r,double[] buffer,geleseninfo gelesen,bool useBuffer){
            if (!useBuffer){ 
                // System.Diagnostics.Trace.WriteLine("sleep");
                return r[index];
            }
            
            while (!(index<gelesen.gelesen)){
                System.Threading.Thread.Sleep(100);
            }
            return buffer[index];
        }
        protected class geleseninfo{
            public int gelesen;
        }
        public static  void solaKontinuierlich(WavReader werte, int blockgroesse, int ueberlappung, int suchbereich, int suchschritt, List<double> beschleunigung, int samplesPerFaktor, List<VideoBeschleuniger.IOSampleBeziehung> sampleFaktoren, VideoBeschleuniger.progressInformation informator,WavWriter writer)
        {
            ueberlappung = (ueberlappung + 1) & ~1;
            var start=DateTime.Now;
            sampleFaktoren.Clear();
            var g = new geleseninfo();
            var parallel = false;
            double[] buffer = null;
            var maxread=8096;
            GC.Collect();
            GC.WaitForPendingFinalizers();
            //PerformanceCounter ramCounter = new PerformanceCounter("Memory", "Available MBytes");
            
            try
            {
                buffer = new double[werte.Count];
                var tmpMemory = new byte[1024 * 1024*100];
                tmpMemory = null;
                GC.Collect();
                parallel = true;
            }catch
            {
                parallel = false;
            }
            //parallel =ramCounter.NextValue()>(100+werte.Count *(werte.header.blockalign+8)/(1024*1024));
            
            if (parallel){
                Console .WriteLine("par speedup");
                buffer=new double[werte.Count];
                Task.Factory.StartNew(()=>{
                    int gelesenspeicher;
                    int anzahl;
                    werte.seekSample(0);
                    while (g.gelesen < werte.Count)
                    {
                        anzahl = Math.Min((int)werte.Count - g.gelesen, maxread);
                        
                        gelesenspeicher=werte.readValues(buffer,g.gelesen,anzahl);
                        g.gelesen += gelesenspeicher;
                        //    System.Diagnostics.Trace.WriteLine(anzahl + " -> " + gelesenspeicher);
                    }
                    /*for (int i=0;i<werte.Count;i++){
                        buffer[i]=werte[i];
                        g.gelesen++;
                    }*/
                });
            }
            
            for (var i = 0; i < beschleunigung.Count; i++) { sampleFaktoren.Add(new VideoBeschleuniger.IOSampleBeziehung()); sampleFaktoren.Last().inputsamplezahl = samplesPerFaktor; }
            System.Diagnostics.Trace.WriteLine(
                $"beschleunigungscount: {beschleunigung.Count()} -> {beschleunigung.Count() * samplesPerFaktor / 22050}s");
            var outputdaten = new ConcurrentQueue<double>();
            var ueberlappbuffer = new double[ueberlappung, 2];
            var bufferpositionen = new List<int>();
            double fehler = 0;
            double blockBedingung = blockgroesse + ueberlappung;
            var fertig = false;
            var zuschreiben = 0;
            var WriteTask = Task.Factory.StartNew(() =>
            {
                double tmp;
                var geschrieben = 0;
                while (!fertig||outputdaten.Count>0)
                {
                    while (outputdaten.Count > 0)
                    {
                        while (!outputdaten.TryDequeue(out tmp))
                        {
                            System.Threading.Thread.Sleep(100);
                        }
                        writer.write(tmp);
                        geschrieben++;
                    }
                    System.Threading.Thread.Sleep(100);
                }
                System.Diagnostics.Trace.WriteLine($"Geschrieben {geschrieben} -> {geschrieben / 22050}");
            });
            
            for (var i = 0; i < beschleunigung.Count; i++)
            { 
                var  addition = 1 / beschleunigung[i];
                for (var n = 0; n < samplesPerFaktor; n++)
                {
                    fehler += addition;
                    while (fehler >= blockBedingung)
                    {

                        fehler -= blockBedingung;
                        bufferpositionen.Add(i * samplesPerFaktor + n);//- (int)blockBedingung+1);

                        for (var k = bufferpositionen.Last(); k < bufferpositionen.Last() + blockBedingung; k++)
                        {
                            var pos = (int)Math.Floor(k / (double)samplesPerFaktor);
                            if (pos >= sampleFaktoren.Count) { break; }
                            
                            sampleFaktoren[pos].outputsamplezahl++;
                        }
                    }
                }
                informator.Invoke(null, .250 * i / beschleunigung.Count);
            }

            System.Diagnostics.Trace.WriteLine(
                $"Bufferpositionen: {bufferpositionen.Count} -> {bufferpositionen.Count * (blockgroesse + ueberlappung) / 22050}s blockgröße: {blockgroesse} ueberlappung: {ueberlappung}");


            var sequenzen = bufferpositionen.Count;
            
            var blockfaktor = (double)werte.Count / sequenzen;
            int startpos;
            var versatz = 0;
            var suchsteps = suchbereich / suchschritt;

            for (var i = 0; i < sequenzen; i++)
            {
                informator.Invoke(null, .25 + .75 * i / sequenzen);
             
                startpos = bufferpositionen[i] + versatz;
                if (i != 0)
                {
                    for (var k = 0; k < ueberlappung; k++)
                    {
                        //writer.write ((ueberlappbuffer[k, 0] * (ueberlappung - k - 1) + ueberlappbuffer[k, 1] * (k)) / ueberlappung);
                        outputdaten.Enqueue((ueberlappbuffer[k, 0] * (ueberlappung - k - 1) + ueberlappbuffer[k, 1] * k) / ueberlappung);
                        zuschreiben++;
                    }
                }
                
                if (i != sequenzen - 1)
                {
                    for (var n = 0; n < blockgroesse + ueberlappung; n++)
                    {
                        if (n + startpos >= werte.Count)
                            continue;
                        
                        if (n < blockgroesse)
                        {
                            //writer.write (werte[startpos + n]);
                            //outputdaten.Enqueue(werte[startpos + n]);
                            outputdaten.Enqueue(ReadQuickly(startpos + n,werte,buffer,g,parallel));
                            zuschreiben++;
                        }
                        else
                        {
                            ueberlappbuffer[n - blockgroesse, 0] = ReadQuickly(startpos + n, werte, buffer, g, parallel);
                        }
                    }

                    //List<double> korellationen = new List<double>();
                    var korellationen=new double[suchsteps*2+1];
                    int nextblock;
                    var testbuffer = new double[suchsteps * 2 + 1, ueberlappbuffer.GetLength(0)];
                    var korrellator = new Action<int>( n=>
                    {

                        var internnextblock = bufferpositionen[i + 1] + n * suchschritt - ueberlappung;
                        for (var k = 0; k < ueberlappung; k++)
                        {
                            if (k + internnextblock < 0 || k + internnextblock >= werte.Count)
                            {
                                testbuffer[n + suchsteps, k] = 0;
                            }
                            else
                            {
                                testbuffer[n + suchsteps, k] = ReadQuickly(k + internnextblock, werte, buffer, g, parallel);//werte[k + nextblock];
                            }

                        }
                        //korellationen.Add(korellation(ueberlappbuffer, false));
                        korellationen[n + suchsteps] = korellation(ueberlappbuffer,0,testbuffer,n+suchsteps, false);
                    });
                    if (!parallel)
                        for (var n = -suchsteps; n <= suchsteps; n++)
                            korrellator.Invoke(n);
                    else 
                        Parallel.For(-suchsteps, suchsteps + 1, korrellator);

                    //versatz = (korellationen.IndexOf(korellationen.Max()) - suchsteps) * suchschritt;
                    versatz = (Array.IndexOf(korellationen,korellationen.Max()) - suchsteps) * suchschritt;

                    nextblock = bufferpositionen[i + 1] + versatz - ueberlappung;
       
                    for (var k = 0; k < ueberlappung; k++)
                    {
                        if (k + nextblock < 0 || k + nextblock >= werte.Count)
                        {
                            ueberlappbuffer[k, 1] = 0;
                        }
                        else
                        {
                            ueberlappbuffer[k, 1] = ReadQuickly(k + nextblock, werte, buffer, g, parallel);//werte[k + nextblock];
                        }
                    }
                }
                else
                {
                    for (var n = startpos; n < werte.Count; n++)
                    {
                        //writer.write(werte[n]);
                        outputdaten.Enqueue (ReadQuickly(n, werte, buffer, g, parallel));
                        zuschreiben++;
                    }
                }
            }
            fertig = true;
            System.Diagnostics.Trace.WriteLine("Sola done");
            WriteTask.Wait();
            System.Diagnostics.Trace.WriteLine("SolaWriter done");
            System.Diagnostics.Trace.WriteLine($"Zuschreiben: {zuschreiben}-> {zuschreiben / 22050}s");
           System.Diagnostics.Trace.WriteLine($"Zeitverbrauch von Sola: {DateTime.Now.Subtract(start).TotalSeconds}s");
        }

        protected static double korellation(double[,] werte, bool betrag = true)
        {
            double diffX = 0;
            double diffY = 0;
            double diffGes = 0;
            double avX = 0;
            double avY = 0;
            for (var i = 0; i < werte.GetLength(0); i++)
            {
                avX += werte[i, 0];
                avY += werte[i, 1];
            }
            avX /= werte.Length;
            avY /= werte.Length;
            for (var i = 0; i < werte.GetLength(0); i++)
            {
                diffX += Math.Pow(werte[i, 0] - avX, 2);
                diffY += Math.Pow(werte[i, 1] - avY, 2);
                diffGes += (werte[i, 0] - avX) * (werte[i, 1] - avY);
            }

            var ergebnis = diffGes / (Math.Sqrt(diffX) * Math.Sqrt(diffY));
            if (double.IsNaN(ergebnis)) { ergebnis = 0; }
            return betrag ? Math.Abs(ergebnis) : ergebnis;
        }
        /// <summary>
        /// Diese funktion ist nur zur parallelisierung gedacht und an die internen gegebenheiten von speedup angepasst
        /// </summary>
        /// <param name="original">das array mit dem es zu vergleichen gilt, 2dimensional</param>
        /// <param name="originalindex">array[1...n,originalindex] wird verglichen</param>
        /// <param name="vergleich">das andere array mit dem es zu vergleichen gilt</param>
        /// <param name="vergleichindex">array[vergleichindex,1...n] wird verglichen</param>
        /// <param name="betrag">ob der betrag der korellation zurückgegeben werden soll</param>
        /// <returns></returns>
        protected static double korellation(double[,] original,int originalindex, double[,] vergleich, int vergleichindex, bool betrag = true)
        {
            double diffX = 0;
            double diffY = 0;
            double diffGes = 0;
            double avX = 0;
            double avY = 0;
            for (var i = 0; i < original.GetLength(0); i++)
            {
                avX += original[i,originalindex];
                avY += vergleich[vergleichindex, i];
            }
            avX /=original.Length;
            avY /= original.Length;
            for (var i = 0; i < original.GetLength(0); i++)
            {
                diffX += Math.Pow(original[i,originalindex] - avX, 2);
                diffY += Math.Pow(vergleich[vergleichindex, i] - avY, 2);
                diffGes += (original[i,originalindex] - avX) * (vergleich[vergleichindex, i] - avY);
            }

            var ergebnis = diffGes / (Math.Sqrt(diffX) * Math.Sqrt(diffY));
            if (double.IsNaN(ergebnis)) { ergebnis = 0; }
            return betrag ? Math.Abs(ergebnis) : ergebnis;
        }
    }
}
