﻿using System;
using System.IO;
using System.Runtime.InteropServices;

namespace Vorrennung
{
    public  class WavWriter
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
        waveheader header;
        Stream daten;
        BinaryWriter writer;
        long startpos;
        long position;
        long laenge;
        long samples => laenge >> 1;
        long samplingrate;
        public WavWriter(Stream basis,long startpos,long samplingrate){
            this.startpos = startpos;
            basis.Seek(startpos,SeekOrigin.Begin );
            this.startpos += 44;
            daten = new BufferedStream(basis,  1677216);
            writer = new BinaryWriter(daten);
            writer.Write(new byte[44],0,44);//speicher fürn header reservieren
            this.samplingrate = samplingrate;
        }

        public void write(double daten)
        {
            var wert = (short)(daten * 32766);//nicht 32768 um eine sicherheit von 2 werten vor Überläufen zu haben;
            writer.Write(wert);
            position += 2;
            if (position>laenge){
                laenge = position;
            } 
            // writer.Flush();
        }
        public void close(bool closeinner)
        {
            try{
                var laenge = this.laenge + 44;
                header.data = 1635017060;
                header.fmt = 544501094;
                header.riff = 1179011410;
                header.wave = 1163280727;
                header.filesize = (uint)(laenge - 8);
                header.fmtlength = 16;
                header.channels = 1;
                header.samplerate = (uint)samplingrate;
                header.bytespersecond = (uint)(samplingrate * 2);
                header.blockalign = 2;
                header.bitspersample = 16;
                header.datalength = (uint)(laenge - 44);
                header.formattag = 1;
                daten.Seek(startpos - 44, SeekOrigin.Begin);
                unsafe
                {
                    fixed (waveheader* headerpointer = &header)
                    {
                        var pointer = (byte*)headerpointer; //h ist bereits fixed 
                        for (var i = 0; i < 44; i++)
                        { 
                            // System.Diagnostics.Trace.WriteLine((byte)*(pointer + i));
                            writer.Write(*(pointer + i));
                        }
                    }
                }

                if (closeinner)
                    writer.Close();
                else
                    writer.Flush();
            }
            catch(Exception e)
            {
                System.Diagnostics.Trace.WriteLine(e);
            }
        }
    }
}
