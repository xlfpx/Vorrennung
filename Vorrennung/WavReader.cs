using System;
using System.Threading.Tasks;
using System.IO;
using System.Runtime.InteropServices;

namespace Vorrennung
{
    public class WavReader
    {
        [StructLayout(LayoutKind.Explicit, Size = 44, CharSet = CharSet.Ansi)]
        public struct waveheader
        {
            [FieldOffset(0)]
            public uint riff;
            [FieldOffset(4)]
            public uint filesize;
            [FieldOffset(8)]
            public uint wave;
            [FieldOffset(12)]
            public uint fmt;
            [FieldOffset(16)]
            public uint fmtlength;
            [FieldOffset(20)]
            public short formattag;
            [FieldOffset(22)]
            public short channels;
            [FieldOffset(24)]
            public uint samplerate;
            [FieldOffset(28)]
            public uint bytespersecond;
            [FieldOffset(32)]
            public short blockalign;
            [FieldOffset(34)]
            public short bitspersample;
            [FieldOffset(36)]
            public uint data;
            [FieldOffset(40)]
            public uint datalength;

        }
        struct chunk
        {
            public uint header;
            public uint laenge;
        }
        
        public long samplingrate => header.samplerate;

        Stream daten;
        BinaryReader reader;
        long startpos;
        long position;
        public long laenge;
        public waveheader header;
        public long Count => laenge / header.blockalign;
        /*   public WavReader(Stream t,long startpos)
        {
            daten = new BufferedStream(t,1677216);
            this.startpos = startpos;
            reader = new BinaryReader(daten);
            t.Seek(startpos,SeekOrigin.Begin );
            this.startpos += 44;// die 1. 44 bytes sollen nicht lesbar sein
            position = 0;
            header=new waveheader();
            byte[] tmp;
            tmp = reader.ReadBytes(44);
            int zuskippen = 44;
            int i;
            unsafe
            {
                fixed (waveheader* pointer = &header)
                {
                    byte* bytepointer = (byte*)pointer;
                    for (i = 0; i < 44; i++)
                    {
                        *(bytepointer + i) = tmp[i];
                    }  
                }
            }

            if ((header.bitspersample != 16) || (header.formattag != 1))
            {
                throw new ArgumentException("Die Wavdaten sind nicht mit 16bit Samples oder nicht in PCM kodiert");
            }
            waveheader h = header;
            MessageBox.Show("bitps: " + h.bitspersample + "\nBlockal: " + h.blockalign + "\nBytps: " + h.bytespersecond + "\nChannels: " + h.channels + "\nData: " + h.data + "\nDatal: " + h.datalength + "\nFilesize: " + h.filesize + "\nFmt: " + h.fmt + "\nFmtl: " + h.fmtlength + "\nFormattag: " + h.formattag + "\nRiff: " + h.riff + "\nSamplerate: " + h.samplerate + "\nWave: " + h.wave + " ");
            laenge = header.filesize+8-44;
        }*/

        public WavReader(Stream t,long startpos)
        {
            daten =  new BufferedStream(t, 1677216);
            daten.Seek(startpos,SeekOrigin.Begin );
            reader=new BinaryReader (daten);
            uint zuskippen=0;
            this.startpos = startpos;
            header = new waveheader();
            var ch=new chunk();
            do{
                // MessageBox.Show("Header lesend...");
                ch.header =(uint) reader.ReadInt32();
                ch.laenge = (uint)reader.ReadInt32();
                // MessageBox.Show("Chunk gelesen...");
                zuskippen += 8 ;
                // MessageBox.Show(""+ch.laenge+" "+ch.header);
                if (ch.header == 1179011410)
                {
                    // MessageBox.Show("RIFF gefunden");
                    header.riff = ch.header;
                    header.filesize = ch.laenge;
                    header.wave = (uint)reader.ReadInt32();
                    zuskippen += 4;
                }else if (ch.header==544501094){//fmt
                    // MessageBox.Show("FMT gefunden "+ch.laenge);
                    unsafe
                    {
                        fixed (waveheader* headerpointer = &header)
                        {
                            var zieladdr = (byte*)headerpointer;
                            int i;
                            for (i = 20; i < 36; i++)
                            {
                                *(zieladdr + i) = reader.ReadByte();
                            }
                        }
                    }
                    if (ch.laenge > 16)
                    {
                        reader.ReadBytes((int)(ch.laenge - 16));
                    }
                    header.fmt = ch.header;
                    header.fmtlength = ch.laenge;
                    zuskippen += ch.laenge;
                }
                else if (ch.header == 1635017060)
                {
                    //  MessageBox.Show("DATA gefunden");
                    header.data = ch.header;
                    header.datalength = ch.laenge;
                    break;
                }
                else
                {
                    // MessageBox.Show("WTF gefunden (wahrscheinlich list) "+ch.header+" "+ch.laenge);
                    zuskippen += ch.laenge;
                    reader.ReadBytes((int)ch.laenge);
                }
                
            }while(true);
            // var h = header;
            // MessageBox.Show("bitps: " + h.bitspersample + "\nBlockal: " + h.blockalign + "\nBytps: " + h.bytespersecond + "\nChannels: " + h.channels + "\nData: " + h.data + "\nDatal: " + h.datalength + "\nFilesize: " + h.filesize + "\nFmt: " + h.fmt + "\nFmtl: " + h.fmtlength + "\nFormattag: " + h.formattag + "\nRiff: " + h.riff + "\nSamplerate: " + h.samplerate + "\nWave: " + h.wave + " ");
            laenge = header.filesize + 8 - zuskippen;
            this.startpos += zuskippen;
            position = 0;
            seek();
        }
        
        public double readNext()
        {
            if (position >= laenge) { throw new IndexOutOfRangeException("Die Datei ist vorbei"); }
            double sampletmp=0;
            for (var i = 0; i < header.channels;i++ )
                sampletmp += reader.ReadInt16()/32768.0f/header.channels;
            
            //System.Diagnostics.Trace.WriteLine(sampletmp);
            position+=header.blockalign;
            //seek();
            return sampletmp / header.channels;
        }
        public void seekSample(long position)
        {
            this.position = position * header.blockalign;
            if (position >= laenge) { throw new IndexOutOfRangeException(
                $"Die Datei enthält nur {position / header.blockalign} Samples, jedoch wurde das {position}.te Sample angefragt"); }
            seek();
        }
        private void seek()
        {
            reader.BaseStream.Seek(position + startpos, SeekOrigin.Begin);
        }
        public double this[long index]
        {
            get
            {
                if (index * header.blockalign != position) { seekSample(index); }
                return readNext();
            }
        }
        public void close()
        {
            reader.Close();
        }
        
        public double[] readValues(int count)
        {
            Console.WriteLine($"Count: {count}");
            var ergebnis = new double[count];
            //position += header.blockalign * count;
            var gelesen = 0;
            while (gelesen < count)
            {
                gelesen += readValues(ergebnis, gelesen, count - gelesen);
                if (gelesen < count) { System.Threading.Thread.Sleep(100); }
            }
            //byte[] werte = reader.ReadBytes(header.blockalign * count);
            /*Parallel.For(0, count, i =>
            {
            // for (int i=0;i<count;i++){
                double value = 0;
                int w=0;
                for (int c = 0; c < header.channels; c++)
                {
                    w = BitConverter.ToInt16(werte, i * header.blockalign + c * 2);//(short)((werte[i*header.blockalign+c*2]<<8) |werte[i*header.blockalign+c*2+1]);
                    value += w / 32768.0f / header.channels;
                }
                ergebnis[i] = value;
              //  System.Diagnostics.Trace.WriteLine(value);
            });*/
           return ergebnis;
        }
        public int readValues(double[] ziel,int startindex,int count)
        {
            //double[] ergebnis = new double[count];
            position += header.blockalign * count;
            var werte = reader.ReadBytes(header.blockalign * count);
            
            Parallel.For(0, werte.Length / header.blockalign, i =>
            {
                // for (int i=0;i<count;i++){
                double value = 0;
                var w = 0;
                for (var c = 0; c < header.channels; c++)
                {
                    w = BitConverter.ToInt16(werte, i * header.blockalign + c * 2);//(short)((werte[i*header.blockalign+c*2]<<8) |werte[i*header.blockalign+c*2+1]);
                    value += w / 32768.0f / header.channels;
                }
                ziel[i+startindex] = value;
              
                // System.Diagnostics.Trace.WriteLine(value);
            }); 
            // System.Diagnostics.Trace.WriteLine("werte: " + werte.Length + " count: " + count+" header.blockalign: "+header.blockalign );
            return werte.Length / header.blockalign;
        }
    }
}
