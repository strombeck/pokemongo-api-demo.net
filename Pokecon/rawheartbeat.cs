using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Google.Common.Geometry;
using Google.Protobuf;

namespace Pokecon
{
	public static class rawheartbeat
	{
		public static readonly float FloatLat = 28.544393f; // work
		public static readonly float FloatLong = -81.503825f; // work

		public static readonly long CoordsLatitude = (long)FloatLat;
		public static readonly long CoordsLongitude = (long)FloatLong;

		public static ulong fti(float f)
		{
			var b = BitConverter.GetBytes(f);
			var l = Convert.ToUInt64(b);
			return l;
		}

		private static void EncodeVarint(ICollection<char> write, ulong value)
		{
			var bits = value & 0x7f;
			value >>= 7;
			while (value != 0)
			{
				write.Add((char)(0x80 | bits));
				bits = value & 0x7f;
				value >>= 7;
			}
			write.Add((char) bits);
		}

		private static string Encode(ulong cellid)
		{
			var output = new List<char>();
			EncodeVarint(output, cellid);
			return string.Join("", output);
		}

		public static List<ulong> GetNeighbors()
		{
			var origin = S2CellId.FromLatLng(S2LatLng.FromDegrees(FloatLat, FloatLong)).ParentForLevel(15);
			
			var walk = new List<ulong> {origin.Id};
			// 10 before and 10 after
			var next = origin.Next;
			var prev = origin.Previous;
			for (var i = 0; i < 10; i++)
			{
				walk.Add(prev.Id);
				walk.Add(next.Id);
				next = next.Next;
				prev = prev.Previous;
			}
			return walk;
		}

		public static ResponseEnvelop.Types.HeartbeatPayload get_heartbeat(string apiEndpoint, string accessToken, ResponseEnvelop response)
		{
			if (response == null) throw new ArgumentNullException(nameof(response));
			var currentTimeRequest = new RequestEnvelop.Types.Requests();

			var epoch = new DateTime(1970, 1, 1);
			var totalSeconds = (uint)(DateTime.UtcNow - epoch).TotalSeconds;
			var currentTimeMessage = new RequestEnvelop.Types.MessageSingleInt
			{
				F1 = totalSeconds
			};
			currentTimeRequest.Message = currentTimeMessage.ToByteString();

			var keyRequest = new RequestEnvelop.Types.Requests();
			var keyMessage = new RequestEnvelop.Types.MessageSingleString
			{
				F1 = "05daf51635c82611d1aac95c0b051d3ec088a930"
			};

			keyRequest.Message = keyMessage.ToByteString();

			var walk = GetNeighbors().OrderBy(c => c);

			var m1 = new RequestEnvelop.Types.Requests {Type = 106};

			var quadMessage = new RequestEnvelop.Types.MessageQuad
			{
				F1 = ByteString.CopyFromUtf8(string.Join("", walk.Select(c => Encode(c).ToArray()))),
				F2 = ByteString.CopyFrom(Enumerable.Repeat((byte) 0, 21).ToArray()),
				// TODO: how to get these?
				Lat = CoordsLatitude,
				Long = CoordsLongitude
			};

			m1.Message = quadMessage.ToByteString();

			response = get_profile(
				accessToken,
				apiEndpoint,
				new List<RequestEnvelop.Types.Requests>
				{
					m1,
					new RequestEnvelop.Types.Requests(),
					currentTimeRequest,
					new RequestEnvelop.Types.Requests(),
					keyRequest
				});

			var payload = response.Payload[0];

			var heartbeat = ResponseEnvelop.Types.HeartbeatPayload.Parser.ParseFrom(payload.ToByteArray());

			return heartbeat;
		}


		public static ResponseEnvelop get_profile(string accessToken, string apiEndpoint, IReadOnlyList<RequestEnvelop.Types.Requests> reqq)
		{
			var req = new RequestEnvelop();
			var req1 = new RequestEnvelop.Types.Requests {Type = 2};
			req.Requests.Add(req1);
			if (reqq.Count >= 1)
			{
				req1.MergeFrom(reqq[0]);
			}

			var req2 = new RequestEnvelop.Types.Requests { Type = 126 };
			if (reqq.Count >= 2)
			{
				req2.MergeFrom(reqq[1]);
			}

			var req3 = new RequestEnvelop.Types.Requests { Type = 4 };
			if (reqq.Count >= 3)
			{
				req3.MergeFrom(reqq[2]);
			}

			var req4 = new RequestEnvelop.Types.Requests { Type = 129 };
			if (reqq.Count >= 4)
			{
				req4.MergeFrom(reqq[3]);
			}

			var req5 = new RequestEnvelop.Types.Requests { Type = 5 };
			if (reqq.Count >= 5)
			{
				req5.MergeFrom(reqq[4]);
			}

			return Program.api_req(apiEndpoint, accessToken, req);
		}
	}
}
