// Rob Holme (rob@holme.com.au)
// 01/06/2015

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Text.RegularExpressions;

namespace HL7ListenerApplication
{
    
    // this provides basic access to a HL7 message. It is stored as string array of segments, no awarness of the message schema.
    class HL7Message
    {
        private string[] segments;
        private string message;
        private char fieldDelimeter;
        private char componentDelimeter;
        private char subComponentDelimer;
        private char repeatDelimeter;
        
        /// <summary>
        /// Constructor. Set the field, component, subcompoenent and repeat delimeters. Throw an exception if the messsage  does not include a MSH segment.
        /// </summary>
        /// <param name="message"></param>
        public HL7Message(string Message)
        {

            message = Message;
            segments = Message.Split((char)0x0D);
            // set the field, component, sub component and repeat delimters
            int startPos = message.IndexOf("MSH");
            if (startPos >= 0)
            {
                startPos = startPos + 2; 
                this.fieldDelimeter = message[startPos + 1];
                this.componentDelimeter = message[startPos + 2];
                this.repeatDelimeter = message[startPos + 3];
                this.subComponentDelimer = message[startPos + 5];
            }
            // throw an exception if a MSH segment is not included in the message. 
            else
            {
                throw new ArgumentException("MSH segment not present.");
            }
        }


        /// <summary>
        /// returns th field delimeter character
        /// </summary>
        public char FieldDelimeter
        {
            get {return this.fieldDelimeter;}
        }

        /// <summary>
        /// returns the component delimeter character
        /// </summary>
        public char ComponentDelimter
        {
            get { return this.componentDelimeter; }
        }


        /// <summary>
        /// returns the sub component delimeter character
        /// </summary>
        public char SubcomponentDelimer
        {
            get { return this.subComponentDelimer; }
        }


        /// <summary>
        /// return the repeat delimeter character
        /// </summary>
        public char RepeatDelimeter
        {
            get { return this.repeatDelimeter; }
        }

        
        /// <summary>
        /// return all message segments as a single string (with 'carriage return' delimiting each segment).  
        /// </summary>
        /// <returns></returns>
        public override string ToString()
        {
            return message;
        }


        /// <summary>
        /// return the value for the coresponding HL7 item. HL7LocationString is formatted as Segment-Field.Componet.SubComponent eg PID-3 or PID-5.1.1
        /// </summary>
        /// <param name="HL7LocationString"></param>
        /// <returns></returns>
        public string[] GetHL7Item(string HL7LocationString)
        {
            string segmentName;
            uint fieldNumber;
            uint componentNumber;
            uint subcomponentNumber;
            uint segmentRepeatNumber;
            uint fieldRepeatNumber;

            if (GetElementPosition(HL7LocationString, out segmentName, out segmentRepeatNumber, out fieldNumber, out fieldRepeatNumber, out componentNumber, out subcomponentNumber)) // GetElement possition return null if the string is not formatted correctly
            {
                if (subcomponentNumber != 0) // segment, field, component and sub component
                {
                    return GetValue(segmentName, fieldNumber, componentNumber, subcomponentNumber, segmentRepeatNumber, fieldRepeatNumber);
                }
                else if (componentNumber != 0) // segment, field and component
                {
                    return GetValue(segmentName, fieldNumber, componentNumber, segmentRepeatNumber, fieldRepeatNumber);
                }
                else if (fieldNumber != 0) // segment and field
                {
                    return GetValue(segmentName, fieldNumber, segmentRepeatNumber, fieldRepeatNumber);
                }
                else if (segmentName != null) // segment only
                {
                    return GetValue(segmentName, segmentRepeatNumber);
                }
                else // this should be redundant, if a value was returned from GetElementPossition it would match one of the earlier if / else if statements.
                {
                    return null;
                }
            }
            else // the user did not provide a valid string identifying a HL7 element
            {
                return null;
            }
        }



        /// <summary>
        /// Return the segments matchting SegmentID. Return as a string array as there may be more than one segment.
        /// </summary>
        /// <param name="SegmentID"></param>
        /// <param name="SegmentRepeatNumber"></param>
        /// <returns></returns>
        private string[] GetValue(string SegmentID, uint SegmentRepeatNumber)
        {
            List<string> segmentsToReturn = new List<string>();
            uint numberOfSegments = 0;

            foreach (string currentLine in this.segments)
            {
                if (Regex.IsMatch(currentLine, "^" + SegmentID, RegexOptions.IgnoreCase)) //search for the segment ID at the start of a line.
                {
                    numberOfSegments++;
                    // if a SegmentRepeaNumber is provided, only add a segment for this specific repeat. Keep cound of the number of segments found.
                    if (SegmentRepeatNumber > 0)
                    {
                        if (SegmentRepeatNumber == numberOfSegments)
                        {
                            segmentsToReturn.Add(currentLine);
                            return segmentsToReturn.ToArray(); // return immediatly, only one segment returned if user specifies a particular segment repeat.
                        }
                    }
                    // add all repeats if SegmentRepeatNumber = 0 (ie not provided).
                    else
                    {
                        segmentsToReturn.Add(currentLine);
                    }
                }
            }
            return segmentsToReturn.ToArray();
        }

        /// <summary>
        /// Return the fields matching FieldID. Return as a string array as the field may be repeating, or the message may contain repeating segments.
        /// </summary>
        /// <param name="SegmentID"></param>
        /// <param name="FieldID"></param>
        /// <param name="SegmentRepeatNumber"></param>
        /// <param name="FieldRepeatNumber"></param>
        /// <returns></returns>
        private string[] GetValue(string SegmentID, uint FieldID, uint SegmentRepeatNumber, uint FieldRepeatNumber)
        {
            List<string> fieldsToReturn = new List<string>();
            string[] fields;
            string[] repeatingFields;

            // get the segment requested
            string[] segments = GetValue(SegmentID, SegmentRepeatNumber);
            // from the segments returned above, look for the fields requested
            if (SegmentID.ToUpper() == "MSH") // MSH segments are a special case, due to MSH-1 being the field delimter character itself.
            {
                FieldID = FieldID - 1; // when splitting MSH segments, MSH-1 is the chracter used in the split, so field numbers won't match the array possition of the split segments as is the case with all other segments.
                if (FieldID == 0) // ie MSH-1
                {
                    fieldsToReturn.Add(fieldDelimeter.ToString()); // return the field demiter if looking for MSH-1
                    return fieldsToReturn.ToArray();
                }
                if (FieldID == 1) // i.e MSH-2
                {
                    if (segments.Length > 0) // make sure a MSH segment was found, otherwise an array out of bound exception would be thrown.
                    {
                        fieldsToReturn.Add(segments[0].ToString().Substring(4, 4)); // special case for MSH-2 as this field contains the repeat demiter. If this is not handled here, the field would be incorrectly treated as a repeating field.
                        return fieldsToReturn.ToArray();
                    }
                }
            }
            // for all segments, return the field(s) requested.
            for (int i = 0; i < segments.Count(); i++)
            {
                string currentField;
                fields = segments[i].Split(fieldDelimeter);
                if (FieldID < fields.Length)
                {
                    if (fields[FieldID].Contains(repeatDelimeter.ToString()))
                    {
                        repeatingFields = fields[FieldID].Split(repeatDelimeter);
                        for (uint j = 0; j < repeatingFields.Count(); j++)
                        {
                            currentField = repeatingFields[j];
                            // if the user has specified a specific field repeat, only return that field.
                            if (FieldRepeatNumber > 0)
                            {
                                if (FieldRepeatNumber == j + 1)
                                {
                                    fieldsToReturn.Add(currentField);
                                    return fieldsToReturn.ToArray(); 
                                }
                            }
                            // else return all of the repeating fields
                            else
                            {
                                fieldsToReturn.Add(currentField);
                            }
                        }
                    }
                    // no repeats detected, so add the single field to return
                    else
                    {
                        if (FieldRepeatNumber <= 1) // since no repeats found, only return a value if user did not specify a specific repeat, or asked for repeat 1. If the user asked for repeats other than the first, nothing will be returned.
                        {
                            fieldsToReturn.Add(fields[FieldID]);
                        }
                    }
                }
            }
            return fieldsToReturn.ToArray();
        }


        /// <summary>
        /// Return the componets matching SegmentID. Return as a string array as the segment may belong to a repeating field or repeating segment.
        /// </summary>
        /// <param name="SegmentID"></param>
        /// <param name="FieldID"></param>
        /// <param name="ComponentID"></param>
        /// <param name="SegmentRepeatNumber"></param>
        /// <param name="FieldRepeatNumber"></param>
        /// <returns></returns>
        private string[] GetValue(string SegmentID, uint FieldID, uint ComponentID, uint SegmentRepeatNumber, uint FieldRepeatNumber)
        {
            List<string> componentsToReturn = new List<string>();
            string[] components;

            // get the field requested
            string[] fields = GetValue(SegmentID, FieldID, SegmentRepeatNumber, FieldRepeatNumber);
            // from the list of fields returned, look for the componeent requested.
            for (int i = 0; i < fields.Count(); i++)
            {
                components = fields[i].Split(componentDelimeter);
                if ((components.Count() >= ComponentID) && (components.Count() > 1))
                {
                    componentsToReturn.Add(components[ComponentID - 1]);
                }
            }
            return componentsToReturn.ToArray();
        }


        /// <summary>
        /// Return the sub components matching SubComponetID. Return as a string array as the sub component may belong to a repeating field or repeating segment.
        /// </summary>
        /// <param name="SegmentID"></param>
        /// <param name="FieldID"></param>
        /// <param name="ComponentID"></param>
        /// <param name="SubComponentID"></param>
        /// <param name="SegmentRepeatNumber"></param>
        /// <param name="FieldRepeatNumber"></param>
        /// <returns></returns>
        private string[] GetValue(string SegmentID, uint FieldID, uint ComponentID, uint SubComponentID, uint SegmentRepeatNumber, uint FieldRepeatNumber)
        {
            List<string> subComponentsToReturn = new List<string>();
            string[] subComponents;

            // get the component requested
            string[] components = GetValue(SegmentID, FieldID, ComponentID, SegmentRepeatNumber, FieldRepeatNumber);
            // from the component(s) returned above look for the subcomponent requested
            for (int i = 0; i < components.Count(); i++)
            {
                subComponents = components[i].Split(this.subComponentDelimer);
                if ((subComponents.Count() >= SubComponentID) && (subComponents.Count() > 1)) // make sure the subComponentID requested exists in the array before requesting it. 
                {
                    subComponentsToReturn.Add(subComponents[SubComponentID - 1]);
                }
            }
            return subComponentsToReturn.ToArray();
        }

        /// <summary>
        /// retrieve the individual segment, field, component, and subcomponent elements from the H7 location string.
        /// </summary>
        /// <param name="HL7LocationString"></param>
        /// <param name="Segment"></param>
        /// <param name="SegmentRepeat"></param>
        /// <param name="Field"></param>
        /// <param name="FieldRepeat"></param>
        /// <param name="Component"></param>
        /// <param name="SubComponent"></param>
        /// <returns></returns>
        private bool GetElementPosition(string HL7LocationString, out string Segment, out uint SegmentRepeat, out uint Field, out uint FieldRepeat, out uint Component, out uint SubComponent)
        {
            string[] tempString;
            string[] tempString2;
            // set all out values to return to negative results, only set values if  found in HL7LocationString
            Segment = null;
            Field = 0;
            Component = 0;
            SubComponent = 0;
            SegmentRepeat = 0;
            FieldRepeat = 0;
            //  use regular expressions to determine what filter was provided
            if (Regex.IsMatch(HL7LocationString, "^[A-Z]{2}([A-Z]|[0-9])([[]([1-9]|[1-9][0-9])[]])?(([-][0-9]{1,3}([[]([1-9]|[1-9][0-9])[]])?[.][0-9]{1,3}[.][0-9]{1,3})|([-][0-9]{1,3}([[]([1-9]|[1-9][0-9])[]])?[.][0-9]{1,3})|([-][0-9]{1,3}([[]([1-9]|[1-9][0-9])[]])?))?$", RegexOptions.IgnoreCase)) // segment([repeat])? or segment([repeat)?-field([repeat])? or segment([repeat)?-field([repeat])?.component or segment([repeat)?-field([repeat])?.component.subcomponent 
            {
                // check to see if a segment repeat number is specified
                Match checkRepeatingSegmentNumber = System.Text.RegularExpressions.Regex.Match(HL7LocationString, "^[A-Z]{2}([A-Z]|[0-9])[[][1-9]{1,3}[]]", RegexOptions.IgnoreCase);
                if (checkRepeatingSegmentNumber.Success == true)
                {
                    string tmpStr = checkRepeatingSegmentNumber.Value.Split('[')[1];
                    SegmentRepeat = UInt32.Parse(tmpStr.Split(']')[0]);

                }
                // check to see if a field repeat number is specified
                Match checkRepeatingFieldNumber = System.Text.RegularExpressions.Regex.Match(HL7LocationString, "[-][0-9]{1,3}[[]([1-9]|[1-9][0-9])[]]", RegexOptions.IgnoreCase);
                if (checkRepeatingFieldNumber.Success == true)
                {
                    string tmpStr = checkRepeatingFieldNumber.Value.Split('[')[1];
                    FieldRepeat = UInt32.Parse(tmpStr.Split(']')[0]);
                }
                // retrieve the field, component and sub componnent values. If they don't exist, set to 0
                tempString = HL7LocationString.Split('-');
                Segment = tempString[0].Substring(0, 3); // the segment name
                if (tempString.Count() > 1) // confirm values other than the segment were provided.
                {
                    tempString2 = tempString[1].Split('.');
                    if (tempString2.Count() >= 1) // field exists, possibly more. Set the field value.
                    {
                        Field = UInt32.Parse(tempString2[0].Split('[')[0]); // if the field contains a repeat number, ignore the repeat value and braces
                    }
                    if (tempString2.Count() >= 2) // field and component, possibly more. Set the component value
                    {
                        Component = UInt32.Parse(tempString2[1]);
                    }
                    if (tempString2.Count() == 3) // field, compoment and sub component exist. Set the value of thesub component.
                    {
                        SubComponent = UInt32.Parse(tempString2[2]);
                    }
                }
                return true;
            }
            else // no valid HL7 element string detected.
            {
                return false;
            }
        }



        /// <summary>
        /// Return the message trigger of the HL7 message. 
        /// </summary>
        /// <param name="message"></param>
        /// <returns></returns>
        public string GetMessageTrigger()
        {
            return this.GetHL7Item("MSH-9.1")[0] + "^" + this.GetHL7Item("MSH-9.2")[0];
        }
    }
}
