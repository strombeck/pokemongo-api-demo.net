using CommandLineParser.Arguments;
using Geocoding.Google;
using Google.Protobuf;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;

namespace Pokecon
{
    // https://www.reddit.com/r/pokemongodev/
    // https://github.com/tejado/pokemongo-api-demo

    public class Program
    {
        const string API_URL = "https://pgorelease.nianticlabs.com/plfe/rpc";
        const string LOGIN_URL = "https://sso.pokemon.com/sso/login?service=https%3A%2F%2Fsso.pokemon.com%2Fsso%2Foauth2.0%2FcallbackAuthorize";
        const string LOGIN_OAUTH = "https://sso.pokemon.com/sso/oauth2.0/accessToken";

        static bool _debug = true;
        static ulong COORDS_LATITUDE = f2i(38.8791981);
        static ulong COORDS_LONGITUDE = f2i(-76.9818437);
        static ulong COORDS_ALTITUDE = 0;

        static void set_location(string locationName)
        {
            var geocoder = new GoogleGeocoder() { };
            var loc = geocoder.Geocode(locationName).FirstOrDefault();
            Console.WriteLine("[!] Your given location: {0}", loc.FormattedAddress);
            Console.WriteLine("[!] lat/long/alt: {0} {1} {2}", loc.Coordinates.Latitude, loc.Coordinates.Longitude, 0);
            set_location_coords(loc.Coordinates.Latitude, loc.Coordinates.Longitude, 0);
        }

        static ulong f2i(double value) { return BitConverter.ToUInt64(BitConverter.GetBytes(value), 0); }

        static void set_location_coords(double latitude, double longitude, double alt)
        {
            COORDS_LATITUDE = f2i(latitude);
            COORDS_LONGITUDE = f2i(longitude);
            COORDS_ALTITUDE = f2i(alt);
        }

        public static ResponseEnvelop api_req(string api_endpoint, string access_token, RequestEnvelop reqs)
        {
            try
            {
                var envelop = new RequestEnvelop
                {
                    Unknown1 = 2,
                    RpcId = 8145806132888207460,
                    Latitude = COORDS_LATITUDE,
                    Longitude = COORDS_LONGITUDE,
                    Altitude = COORDS_ALTITUDE,
                    Unknown12 = 989,
                    Auth = new RequestEnvelop.Types.AuthInfo
                    {
                        Provider = "ptc",
                        Token = new RequestEnvelop.Types.AuthInfo.Types.JWT { Contents = access_token, Unknown13 = 59 }
                    }
                };
	            foreach (var r in reqs.Requests)
	            {
		            envelop.Requests.Add(r);
	            }
	            using (var client = new HttpClient())
                {
                    client.DefaultRequestHeaders.Add("User-Agent", "Niantic App");
                    using (var ms = new MemoryStream())
                    {
                        envelop.WriteTo(ms);
                        ms.Position = 0;
                        var result = client.PostAsync(api_endpoint, new ByteArrayContent(ms.ToArray())).Result;
                        var r = result.Content.ReadAsByteArrayAsync().Result;
                        var ret = ResponseEnvelop.Parser.ParseFrom(r);
                        return ret;
                    }
                }
            }
            catch (Exception e)
            {
                if (_debug)
                    Console.WriteLine(e);
                return null;
            }
        }

        static string get_api_endpoint(string access_token)
        {
			var apiEnpointRequest = new RequestEnvelop();

	        apiEnpointRequest.Requests.Add(new RequestEnvelop.Types.Requests {Type = 2});
	        apiEnpointRequest.Requests.Add(new RequestEnvelop.Types.Requests {Type = 126});
		    apiEnpointRequest.Requests.Add(new RequestEnvelop.Types.Requests {Type = 4});
			apiEnpointRequest.Requests.Add(new RequestEnvelop.Types.Requests {Type = 129});
			apiEnpointRequest.Requests.Add(new RequestEnvelop.Types.Requests
			{
				Type = 5,
				Message = new RequestEnvelop.Types.Unknown3
				{
					Unknown4 = "4a2e9bc330dae60e7b74fc85b98868ab4700802e"
				}.ToByteString()
			});

			var ret = api_req(API_URL, access_token, apiEnpointRequest);
            try { return "https://" + ret.ApiUrl + "/rpc"; }
            catch { return null; }
        }

        static ResponseEnvelop get_profile(string api_endpoint, string access_token)
        {
			var req = new RequestEnvelop();
	        var req1 = new RequestEnvelop.Types.Requests {Type = 2};
	        req.Requests.Add(req1);

			return api_req(api_endpoint, access_token, req);
        }

        static string login_ptc(string username, string password)
        {
            Console.WriteLine("[!] login for: {0}", username);

            using (var clientHandler = new HttpClientHandler())
            {
                clientHandler.AllowAutoRedirect = false;
                using (var client = new HttpClient(clientHandler))
                {
                    client.DefaultRequestHeaders.Add("User-Agent", "niantic");
                    var r = client.GetAsync(LOGIN_URL).Result.Content.ReadAsStringAsync().Result;
                    var jdata = JObject.Parse(r);
                    var data = new[]
                    {
                        new KeyValuePair<string, string>("lt", (string)jdata["lt"]),
                        new KeyValuePair<string, string>("execution", (string)jdata["execution"]),
                        new KeyValuePair<string, string>("_eventId", "submit"),
                        new KeyValuePair<string, string>("username", username),
                        new KeyValuePair<string, string>("password", password),
                    };
                    var result = client.PostAsync(LOGIN_URL, new FormUrlEncodedContent(data)).Result;
                    if (result.Headers.Location == null)
                        return null;
                    var location = result.Headers.Location.ToString();
                    var r1 = result.Content.ReadAsStringAsync().Result;

                    string ticket = null;
                    try { ticket = new Regex(".*ticket=").Split(location)[1]; }
                    catch
                    {
                        if (_debug)
                            Console.WriteLine(r1);
                        return null;
                    }

                    var data1 = new[]
                    {
                        new KeyValuePair<string, string>("client_id", "mobile-app_pokemon-go"),
                        new KeyValuePair<string, string>("redirect_uri", "https://www.nianticlabs.com/pokemongo/error"),
                        new KeyValuePair<string, string>("client_secret", "w8ScCUXJQc6kXKw8FiOhd8Fixzht18Dq3PEVkUCP5ZPxtgyWsbTvWHFLm2wNY0JR"),
                        new KeyValuePair<string, string>("grant_type", "refresh_token"),
                        new KeyValuePair<string, string>("code", ticket),
                    };
                    var r2 = client.PostAsync(LOGIN_OAUTH, new FormUrlEncodedContent(data1)).Result.Content.ReadAsStringAsync().Result;
                    var access_token = new Regex("&expires.*").Split(r2)[0];
                    access_token = new Regex(".*access_token=").Split(access_token)[1];
                    return access_token;
                }
            }
        }

//	    static string raw_heartbeat(string api_endpoint, string access_token, string response)
//	    {
//		    var m4 = new RequestEnvelop();
//		    var m = RequestEnvelop.AltitudeFieldNumber........

//			m.f1 = int
//		    (time.time()*1000)
//		    m4.message = m.SerializeToString()
//		    m5 = pokemon_pb2.RequestEnvelop.Requests()
//		    m = pokemon_pb2.RequestEnvelop.MessageSingleString()
//		    m.bytes = "05daf51635c82611d1aac95c0b051d3ec088a930"
//		    m5.message = m.SerializeToString()

//		    walk = sorted(getNeighbors())

//		    m1 = pokemon_pb2.RequestEnvelop.Requests()
//		    m1.type = 106
//		    m = pokemon_pb2.RequestEnvelop.MessageQuad()
//		    m.f1 = ''.join(map(encode, walk))
//		    m.f2 = "\000\000\000\000\000\000\000\000\000\000\000\000\000\000\000\000\000\000\000\000\000"
//		    m.lat = COORDS_LATITUDE
//		    m.
//		    long =
//		    COORDS_LONGITUDE

//		    m1.message = m.SerializeToString()

//		    response = get_profile(
//			    access_token,
//			    api_endpoint,
//			    response.unknown7,
//			    m1,
//			    pokemon_pb2.RequestEnvelop.Requests(),
//			    m4,
//			    pokemon_pb2.RequestEnvelop.Requests(),
//			    m5)

//		    payload = response.payload[0]

//		    heartbeat = pokemon_pb2.ResponseEnvelop.HeartbeatPayload()

//		    heartbeat.ParseFromString(payload)
//		    return heartbeat
//	    }

//	    def heartbeat(api_endpoint, access_token, response):
//    while True:
//        try:
//            h = raw_heartbeat(api_endpoint, access_token, response)
//            return h
//		except Exception, e:
//            if DEBUG:

//				print(e)

//			print('[-] Heartbeat missed, retrying')


//def scan(api_endpoint, access_token, response, origin, pokemons):

//	steps = 0
//    steplimit = NUM_STEPS
//	pos = 1

//	x   = 0
//    y   = 0
//    dx  = 0
//    dy  = -1
//    while steps<steplimit**2:

//		original_lat = FLOAT_LAT

//		original_long = FLOAT_LONG

//		parent = CellId.from_lat_lng(LatLng.from_degrees(FLOAT_LAT, FLOAT_LONG)).parent(15)


//		h = heartbeat(api_endpoint, access_token, response)

//		hs = [h]
//		seen = set([])
//        for child in parent.children():
//            latlng = LatLng.from_point(Cell(child).get_center())

//			set_location_coords(latlng.lat().degrees, latlng.lng().degrees, 0)
//            hs.append(heartbeat(api_endpoint, access_token, response))

//		set_location_coords(original_lat, original_long, 0)


//		visible = []

//        for hh in hs:
//            for cell in hh.cells:
//                for wild in cell.WildPokemon:
//                    hash = wild.SpawnPointId + ':' + str(wild.pokemon.PokemonId)
//                    if (hash not in seen):
//                        visible.append(wild)
//                        seen.add(hash)

//        for cell in h.cells:
//            if cell.NearbyPokemon:
//                other = LatLng.from_point(Cell(CellId(cell.S2CellId)).get_center())
//                diff = other - origin
//# print(diff)
//				difflat = diff.lat().degrees

//				difflng = diff.lng().degrees
//                if len(cell.NearbyPokemon) > 0:

//					print('[+] Found pokemon!')
//                for poke in cell.NearbyPokemon:

//						print('    (%s) %s' % (poke.PokedexNumber, pokemons[poke.PokedexNumber - 1]['Name']))

//        for poke in visible:
//            other = LatLng.from_degrees(poke.Latitude, poke.Longitude)
//            diff = other - origin
//# print(diff)
//			difflat = diff.lat().degrees

//			difflng = diff.lng().degrees

//			timestamp = int(time.time())

//			add_pokemon(poke.pokemon.PokemonId, pokemons[poke.pokemon.PokemonId - 1]['Name'], poke.Latitude, poke.Longitude, timestamp, poke.TimeTillHiddenMs / 1000)


//		write_data_to_file()

//        if (-steplimit/2 < x <= steplimit/2) and(-steplimit/2 < y <= steplimit/2):

//			set_location_coords((x* 0.0025) + deflat, (y* 0.0025 ) + deflng, 0)
//        if x == y or(x< 0 and x == -y) or(x > 0 and x == 1-y):

//			dx, dy = -dy, dx
//		x, y = x + dx, y+dy
//		  steps +=1


//		print('[+] Scan: %0.1f %%' % (((steps + (pos* .25) - .25) / steplimit**2) * 100))


        public class Options
        {
            [ValueArgument(typeof(string), 'u', "username", Description = "PTC Username", Optional = false)]
            public string username { get; set; }
            [ValueArgument(typeof(string), 'p', "password", Description = "PTC Password", Optional = false)]
            public string password { get; set; }
            [ValueArgument(typeof(string), 'l', "location", Description = "Location", DefaultValue = "1600 pennsylvania ave washington dc")]
            public string location { get; set; }
            [ValueArgument(typeof(bool), 'd', "debug", Description = "Debug Mode", DefaultValue = true)]
            public bool debug { get; set; }
        }

        public static void Main(string[] args1)
        {
            var argsParser = new CommandLineParser.CommandLineParser();
            var args = new Options { };
            argsParser.ExtractArgumentAttributes(args);
            try { argsParser.ParseCommandLine(args1); }
            catch (Exception e) { Console.WriteLine(e.Message); return; }
            
            if (args.debug)
            {
                _debug = true;
                Console.WriteLine("[!] DEBUG mode on");
            }
            if (string.IsNullOrEmpty(args.username))
            {
                Console.WriteLine("[!] DEBUG mode on");
                return;
            }

            set_location(args.location);

            var access_token = login_ptc(args.username, args.password);
            if (access_token == null)
            {
                Console.WriteLine("[-] Wrong username/password");
                return;
            }
            Console.WriteLine("[+] RPC Session Token: {0} ...", access_token);

            var api_endpoint = get_api_endpoint(access_token);
            if (api_endpoint == null)
            {
                Console.WriteLine("[-] RPC server offline");
                return;
            }
            Console.WriteLine("[+] Received API endpoint: {0}", api_endpoint);

            var profile = get_profile(api_endpoint, access_token);
            if (profile != null)
            {
                Console.WriteLine("[+] Login successful");
	            var profile2 = profile.Payload[0].Profile;
                Console.WriteLine("[+] Username: {0}", profile2.Username);
                var creationTime = FromUnixTime(profile2.CreationTime);
                Console.WriteLine("[+] You are playing Pokemon Go since: {0}", creationTime);
	            foreach (var curr in profile2.Currency)
	            {
		            Console.WriteLine("[+] {0}: {1}", curr.Type, curr.Amount);
	            }


	            var heartbeat = rawheartbeat.get_heartbeat(api_endpoint, access_token, profile);

            }
            else {
                Console.WriteLine("[-] Ooops...");
            }
        }

        public static DateTime FromUnixTime(long source)
        {
            return new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddMilliseconds(source);
        }
    }
}
