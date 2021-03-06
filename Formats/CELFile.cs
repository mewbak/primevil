﻿using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace Primevil.Formats
{
    public class CELFile
    {
        private readonly byte[] fileData;
        private readonly bool isCL2;
        //private readonly bool isTileCel;

        private struct FrameInfo
        {
            public int Offset;
            public int Size;
        }

        private readonly List<FrameInfo> frames = new List<FrameInfo>();

        // decoding state
        private byte[] palette;
        private int frameNum;
        private int frameOffset;
        private int frameSize;
        private int filePos;
        private readonly List<byte> decoded = new List<byte>();

        public class Frame
        {
            public int Width;
            public int Height;
            public byte[] Data;
        }


        public CELFile(byte[] fileData, bool isCL2 = false)
        {
            this.fileData = fileData;
            this.isCL2 = isCL2;
            //this.isTileCel = !isCL2;

            var r = new BinaryReader(fileData);
            var first = r.ReadU32();
            if (first == 32) {
                if (isCL2) {
                    r.Seek(0);
                    ReadCL2ArchiveOffsets(r);
                } else {
                    r.Seek(32);
                    for (int i = 0; i < 8; ++i)
                        ReadNormalOffsets(r);
                }
            } else {
                r.Seek(0);
                ReadNormalOffsets(r);
            }

            Debug.WriteLine("NumFrames: " + NumFrames);
        }

        public static CELFile Load(MPQArchive mpq, string path)
        {
            using (var f = mpq.Open(path)) {
                var data = new byte[f.Length];
                var len = f.Read(data, 0, (int)f.Length);
                Debug.Assert(len == f.Length);
                bool isCL2 = path.EndsWith(".cl2");
                return new CELFile(data, isCL2);
            }
        }

        private void ReadCL2ArchiveOffsets(BinaryReader r)
        {
            var headerOffsets = new uint[8];
            for (int i = 0; i < 8; ++i)
                headerOffsets[i] = r.ReadU32();
            for (int i = 0; i < 8; ++i) {
                r.Seek((int)headerOffsets[i]);
                ReadNormalOffsets(r, headerOffsets[i]);
            }
        }

        private void ReadNormalOffsets(BinaryReader r, uint offsetOffset = 0)
        {
            uint numFrames = r.ReadU32();
            var offsets = new uint[numFrames + 1];
            for (var i = 0; i <= numFrames; ++i)
                offsets[i] = r.ReadU32();
            for (var i = 0; i < numFrames; ++i) {
                frames.Add(new FrameInfo {
                    Offset = (int)(offsets[i] + offsetOffset),
                    Size = (int)(offsets[i + 1] - offsets[i])
                });
            }
        }

        public int NumFrames
        {
            get { return frames.Count; }
        }

        public Frame GetFrame(int index, byte[] palette)
        {
            if (index < 0 || index >= NumFrames)
                throw new ArgumentException();

            this.palette = palette;
            frameNum = index;
            frameOffset = frames[index].Offset;
            frameSize = frames[index].Size;
            filePos = frameOffset;
            decoded.Clear();

            if (isCL2) {
                //Debug.WriteLine("CL2");
                //return null;
                return DecodeCL2();
            }

            //Debug.WriteLine("--------");
            //Debug.WriteLine("size: " + frameSize);

            if (frameSize == 1024) {
                //Debug.WriteLine("1024");
                //return null;
                return DecodeRaw32();
            }
            
            if (HasSignature(lt1)) {
                //Debug.WriteLine("lt1");
                //return null;
                return DecodeGtLt(true);
            }
            
            if (HasSignature(gt1)) {
                //Debug.WriteLine("gt1");
                //return null;
                return DecodeGtLt(false);
            }

            //return null;
            //Debug.WriteLine("ELSE");
            return DecodeNormal();
        }

        private Frame DecodeCL2()
        {
            int i = 10; // CL2 frames always have headers

            for(; i < frameSize; ++i)
            {
                // Color command
                if(fileData[frameOffset + i] > 127)
                {
                    int val = 256 - fileData[frameOffset + i];
                   
                    // Regular command
                    if(val <= 65)
                    {
                        int j;
                        // Just push the number of pixels specified by the command
                        for(j = 1; j < val+1 && i+j < frameSize; ++j)
                        {
                            int index = i+j;
                            PutPaletteColor(fileData[frameOffset + index]);
                        }
                        
                        i+= val;
                    }

                    // RLE (run length encoded) Colour command
                    else
                    {
                        for (int j = 0; j < val - 65; j++)
                            PutPaletteColor(fileData[frameOffset + i + 1]);
                        i += 1;
                    }
                }

                // Transparency command
                else
                {
                    // Push transparent pixels
                    FillTransparent(fileData[frameOffset + i]);
                }
            }

            int offset = fileData[frameOffset + 3] << 8 | fileData[frameOffset + 2];
            int width = CL2Width(offset);
            return MakeFrame(width);
        }

        int CL2Width(int offset)
        {
            int pixels = 0;
            int i = 10; // CL2 frames always have headers

            for(; i < frameSize; ++i)
            {
                if(i == offset)
                    return pixels / 32;

                // Color command
                if(fileData[frameOffset + i] > 127)
                {
                    var val = 256 - fileData[frameOffset + i];

                    // Regular command
                    if(val <= 65)
                    {
                        pixels += val;
                        i+= val;
                    }

                    // RLE (run length encoded) Colour command
                    else
                    {
                        pixels += val-65; 
                        i += 1;
                    }
                }

                // Transparency command
                else
                {
                    pixels += fileData[frameOffset + i];
                }
            }

            return -1; // keep the compiler happy
        }

        private Frame DecodeNormal()
        {
            //int offset = 0;
            //bool fromHeader = false;

            // The frame has a header which we can use to determine width
            /*if (!isTileCel && fileData[frameOffset] == 10) {
                fromHeader = true;
                offset = (fileData[frameOffset + 3] << 8 | fileData[frameOffset + 2]);
                filePos += 10; // Skip the header
            }*/

            for (int i = 0; i < frameSize; ++i) {
                int val = fileData[frameOffset + i];
                if (val <= 127) {
                    int off = frameOffset + i + 1;
                    for (int j = 0; j < val; ++j)
                        PutPaletteColor(fileData[off + j]);
                    i += val;
                }
                else {
                    FillTransparent(256 - val);
                }
            }

            //int width = NormalWidth(fromHeader, offset);
            //return MakeFrame(width);
            //Debug.WriteLine("remainder: " + (frameSize % 32));
            return MakeFrame(32);
            /*int diff = 0;
            for (int i = 0; i < 1024; ++i) {
                int w = 32;
                if ((i % 2) == 0)
                    w += diff;
                else
                    w -= diff++;
                if (w > 0 && (frameSize % w) == 0)
                    return MakeFrame(w);
            }

            Debug.WriteLine("frameSize: " + frameSize);

            throw new Exception("unable to find width");*/
        }

        int NormalWidth(bool fromHeader, int offset)
        {
            // If we have a header, we know that offset points to the end of the 32nd line.
            // So, when we reach that point, we will have produced 32 lines of pixels, so we 
            // can divide the number of pixels we have passed at this point by 32, to get the 
            // width.
            if(fromHeader) {
                // Workaround for objcurs.cel, the only cel file containing frames with a header whose offset is zero
                if(offset == 0) {
                    if(frameNum == 0)
                        return 33;
                    if(frameNum > 0 && frameNum <10)
                        return 32;
                    if(frameNum == 10)
                        return 23;
                    if(frameNum > 10 && frameNum < 86)
                        return 28;
                }

                int widthHeader = 0; 

                for(int i = 10; i < frameSize; ++i) {
                    if(i == offset) {
                        widthHeader = widthHeader/32;
                        break;
                    }

                    int val = fileData[frameOffset + i];
                    if (val <= 127) { // Regular command
                        widthHeader += val;
                        i += val;
                    } else { // Transparency command
                        widthHeader += 256 - val;
                    }
                }

                return widthHeader;
            }
        
            // If we do not have a header we probably (definitely?) don't have any transparency.
            // The maximum stretch of opaque pixels following a command byte is 127.
            // Since commands can't wrap over lines (it seems), if the width is shorter than 127,
            // the first (command) byte will indicate an entire line, so it's value is the width.
            // If the width is larger than 127, it will be some sequence of 127 byte long stretches,
            // followed by some other value to bring it to the end of a line (presuming the width is
            // not divisible by 127).
            // So, for all image except those whose width is divisible by 127, we can determine width
            // by looping through control bits, adding 127 each time, until we find some value which
            // is not 127, then add that to the 127-total and that is our width.
            //
            // The above is the basic idea, but there is also a bunch of crap added in to maybe deal
            // with frames that don't quite fit the criteria.

            int widthRegular = 0;
            bool hasTrans = false;

            int lastVal = 0;
            int lastTransVal = 0;

            for (int i = 0; i < frameSize; i++) {
                int val = fileData[frameOffset + i];
                int val1 = fileData[frameOffset + i + 1];

                if (val <= 127) { // Regular command
                    widthRegular += val;
                    i += val;
                    
                    // Workaround for frames that start with a few px, then trans for the rest of the line
                    if (128 <= val1)
                        hasTrans = true;
                } else { // Transparency command
                    // Workaround for frames that start trans, then a few px of colour at the end
                    if (val == lastTransVal && lastVal <= 127 && lastVal == val1)
                        break;

                    widthRegular += 256 - val;
                    
                    // Workaround - presumes all headerless frames first lines start transparent, then go colour,
                    // then go transparent again, at which point they hit the end of the line, or if the first two
                    // commands are both transparency commands, that the image starts with a fully transparent line
                    if ((hasTrans || 128 <= val1) && val != 128)
                        break;

                    hasTrans = true;
                    lastTransVal = val;
                }

                if (val != 127 && !hasTrans)
                    break;

                lastVal = val;
            }

            return widthRegular;
        }



        private Frame DecodeRaw32()
        {
            for (int i = 0; i < frameSize; ++i)
                PutPaletteColor(fileData[frameOffset + i]);
            return MakeFrame(32);
        }

        private Frame DecodeGtLt(bool less)
        {
            DrawRow(0, 15, less);

            if ((less && HasSignature(lt2)) || (!less && HasSignature(gt2))) {
                DrawRow(16, 33, less);
            } else {
                for (int i = 256; i < frameSize; i++)
                    PutPaletteColor(fileData[frameOffset + i]);
            }

            return MakeFrame(32);
        }

        private Frame MakeFrame(int width)
        {
            return new Frame {
                Width = width,
                Height = decoded.Count / (width * 4),
                Data = decoded.ToArray()
            };
        }

        private void DrawRow(int row, int lastRow, bool less)
        {
            for (; row < lastRow; ++row) {
                // Skip markers - for less than, when on the first half of the image (row < 16), all even rows will start with a pair of marker bits
                // for the second half of the image (row >= 16), all odd rows will start with a pair of marker bits.
                // The inverse is true of greater than images.
                if ((less && ((row < 16 && row % 2 == 0) || (row >= 16 && row % 2 != 0))) ||
                   (!less && ((row < 16 && row % 2 != 0) || (row >= 16 && row % 2 == 0))))
                    filePos += 2;

                int toDraw;
                if (row < 16)
                    toDraw = 2 + (row * 2);
                else
                    toDraw = 32 - ((row - 16) * 2);

                if (less)
                    FillTransparent(32 - toDraw);

                for (int i = 0; i < toDraw; ++i)
                    PutPaletteColor(fileData[filePos++]);

                if (!less)
                    FillTransparent(32 - toDraw);
            }
        }

        private void FillTransparent(int count)
        {
            for (int i = 0; i < count; ++i) {
                decoded.Add(0);
                decoded.Add(0);
                decoded.Add(0);
                decoded.Add(0);
            }
        }

        private void PutPaletteColor(int index)
        {
            decoded.Add(palette[3 * index + 0]);
            decoded.Add(palette[3 * index + 1]);
            decoded.Add(palette[3 * index + 2]);
            decoded.Add(255);
        }





        private static int[] gt1 = { 196, 2, 14, 34, 62, 98, 142, 194 };
        private static int[] gt2 = { 196, 254, 318, 374, 422, 462, 494, 518, 534 };
        private static int[] lt1 = { 226, 0, 8, 24, 48, 80, 120, 168, 224 };
        private static int[] lt2 = { 530, 288, 348, 400, 444, 480, 508, 528 };

        private bool HasSignature(int[] desc)
        {
            if (frameSize < desc[0])
                return false;
            for (int i = 0; i < desc.Length - 1; ++i) {
                int off = frameOffset + desc[i + 1];
                if (fileData[off] != 0 || fileData[off + 1] != 0)
                    return false;
            }
            return true;
        }
    }
}
