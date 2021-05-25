/*  Copyright (c) Microsoft Corporation.  All rights reserved. */
/* AUTHOR: Vance Morrison   
 * Date  : 10/20/2007  */
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Xml;

namespace Utilities
{
    /// <summary>
    /// ConfigData is simply a Dictionary that knows how to read and write itself to XML efficiently. 
    /// </summary>
    public class ConfigData : SortedDictionary<string, string>
    {
        public ConfigData() { m_elementName = "ConfigData"; }
        public ConfigData(string xmlFileName, bool autoWrite = false, string elementName = "ConfigData")
        {
            m_elementName = elementName;
            m_fileName = xmlFileName;
            m_autoWrite = autoWrite;
            Read(xmlFileName);
        }
        public ConfigData(Stream xmlDataStream) : this(xmlDataStream, "ConfigData") { }
        public ConfigData(Stream xmlDataStream, string elementName)
        {
            m_elementName = elementName;
            Read(xmlDataStream);
        }

        public new string this[string key]
        {
            get
            {
                string result = null;
                base.TryGetValue(key, out result);
                return result;
            }
            set
            {
                if (value == null)
                {
                    base.Remove(key);
                }
                else
                {
                    base[key] = value;
                }

                if (m_autoWrite && m_fileName != null)
                {
                    Write(m_fileName);
                }
            }
        }
        public double GetDouble(string key, double defaultValue)
        {
            string value;
            if (TryGetValue(key, out value))
            {
                double doubleValue;
                if (double.TryParse(value, out doubleValue))
                {
                    return doubleValue;
                }
            }
            return defaultValue;
        }

        public string ElementName
        {
            get { return m_elementName; }
            set { m_elementName = value; }
        }
        public void Write(string xmlFileName)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(xmlFileName)));
            XmlWriterSettings settings = new XmlWriterSettings();
            using (XmlWriter writer = XmlWriter.Create(xmlFileName, new XmlWriterSettings() { Indent = true, NewLineOnAttributes = true }))
            {
                WriteData(writer);
            }
        }
        public void Write(Stream stream)
        {
            XmlWriterSettings settings = new XmlWriterSettings();
            settings.Indent = true;
            settings.NewLineOnAttributes = true;
            using (XmlWriter writer = XmlWriter.Create(stream, settings))
            {
                writer.Close();
            }
        }
        public void Read(string xmlFileName)
        {
            if (File.Exists(xmlFileName))
            {
                XmlReaderSettings settings = new XmlReaderSettings() { IgnoreWhitespace = true, IgnoreComments = true };
                using (XmlReader reader = XmlTextReader.Create(xmlFileName, settings))
                {
                    Read(reader);
                }
            }
        }
        public void Read(Stream xmlDataStream)
        {

            XmlReaderSettings settings = new XmlReaderSettings() { IgnoreWhitespace = true, IgnoreComments = true };
            using (XmlReader reader = XmlTextReader.Create(xmlDataStream, settings))
            {
                Read(reader);
            }
        }
        public void Read(XmlReader reader)
        {
            int entryDepth = reader.Depth;
            try
            {
                reader.Read();
                for (; ; )
                {
                    switch (reader.NodeType)
                    {
                        case XmlNodeType.Element:
                            // Console.WriteLine("Reader got element " + reader.Name);
                            if (reader.Depth > entryDepth)
                            {
                                string key = reader.Name;
                                string value = reader.ReadElementContentAsString();
                                Add(key, value);
                                continue;
                            }
                            else
                            {
                                if (reader.Name != m_elementName)
                                {
                                    throw new Exception("Unexpected Element " + reader.Name + " expected " + m_elementName);
                                }
                            }
                            break;
                        case XmlNodeType.EndElement:
                            // Console.WriteLine("Reader got End element " + reader.Name);
                            return;
                    }
                    reader.Read();
                }
            }
            catch (Exception)
            {
                Debug.WriteLine("Error reading configuration file.  May have partial results");
            }
        }

        #region Private
        private void WriteData(XmlWriter writer)
        {
            writer.WriteStartElement(m_elementName);
            foreach (KeyValuePair<string, string> entry in this)
            {
                // TODO worry about invalid historyInfo names 
                writer.WriteElementString(entry.Key, entry.Value);
            }
            writer.WriteEndElement();
        }

        private string m_elementName;
        private string m_fileName;
        private bool m_autoWrite;
        #endregion
    }

}