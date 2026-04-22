using Common;
using Modbus.FunctionParameters;
using System;
using System.Collections.Generic;
using System.Net;
using System.Reflection;

namespace Modbus.ModbusFunctions
{
    /// <summary>
    /// Class containing logic for parsing and packing modbus read discrete inputs functions/requests.
    /// </summary>
    public class ReadDiscreteInputsFunction : ModbusFunction
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="ReadDiscreteInputsFunction"/> class.
        /// </summary>
        /// <param name="commandParameters">The modbus command parameters.</param>
        public ReadDiscreteInputsFunction(ModbusCommandParameters commandParameters) : base(commandParameters)
        {
            CheckArguments(MethodBase.GetCurrentMethod(), typeof(ModbusReadCommandParameters));
        }

        /// <inheritdoc />
        public override byte[] PackRequest()
        {
            ModbusReadCommandParameters p = this.CommandParameters as ModbusReadCommandParameters;
            byte[] request = new byte[12];

            Buffer.BlockCopy(BitConverter.GetBytes(IPAddress.HostToNetworkOrder((short)p.TransactionId)), 0, request, 0, 2);
            Buffer.BlockCopy(BitConverter.GetBytes(IPAddress.HostToNetworkOrder((short)p.ProtocolId)), 0, request, 2, 2);
            Buffer.BlockCopy(BitConverter.GetBytes(IPAddress.HostToNetworkOrder((short)p.Length)), 0, request, 4, 2);
            request[6] = p.UnitId;

            request[7] = p.FunctionCode;
            Buffer.BlockCopy(BitConverter.GetBytes(IPAddress.HostToNetworkOrder((short)p.StartAddress)), 0, request, 8, 2);
            Buffer.BlockCopy(BitConverter.GetBytes(IPAddress.HostToNetworkOrder((short)p.Quantity)), 0, request, 10, 2);

            return request;
        }

        /// <inheritdoc />
        public override Dictionary<Tuple<PointType, ushort>, ushort> ParseResponse(byte[] response)
        {
            Dictionary<Tuple<PointType, ushort>, ushort> result = new Dictionary<Tuple<PointType, ushort>, ushort>();

            if ((response[7] & 0x80) != 0)
            {
                HandleException(response[8]);
                return result;
            }

            byte byteCount = response[8];
            ModbusReadCommandParameters p = this.CommandParameters as ModbusReadCommandParameters;

            ushort inputIndex = 0;
            for (int i = 0; i < byteCount; i++)
            {
                byte currentByte = response[9 + i];

                for (int bit = 0; bit < 8; bit++)
                {
                    if (inputIndex >= p.Quantity)
                        break;

                    ushort inputValue = (ushort)((currentByte >> bit) & 0x01);
                    ushort inputAddress = (ushort)(p.StartAddress + inputIndex);

                    result.Add(new Tuple<PointType, ushort>(PointType.DIGITAL_INPUT, inputAddress), inputValue);
                    inputIndex++;
                }
            }

            return result;
        }
    }
    
}