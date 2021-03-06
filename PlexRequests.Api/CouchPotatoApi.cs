﻿#region Copyright
// /************************************************************************
//    Copyright (c) 2016 Jamie Rees
//    File: CouchPotatoApi.cs
//    Created By: Jamie Rees
//   
//    Permission is hereby granted, free of charge, to any person obtaining
//    a copy of this software and associated documentation files (the
//    "Software"), to deal in the Software without restriction, including
//    without limitation the rights to use, copy, modify, merge, publish,
//    distribute, sublicense, and/or sell copies of the Software, and to
//    permit persons to whom the Software is furnished to do so, subject to
//    the following conditions:
//   
//    The above copyright notice and this permission notice shall be
//    included in all copies or substantial portions of the Software.
//   
//    THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND,
//    EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF
//    MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
//    NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE
//    LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION
//    OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION
//    WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
//  ************************************************************************/
#endregion
using System;

using Newtonsoft.Json.Linq;

using NLog;
using PlexRequests.Api.Interfaces;
using PlexRequests.Api.Models.Movie;

using RestSharp;

namespace PlexRequests.Api
{
    public class CouchPotatoApi : ICouchPotatoApi
    {
        public CouchPotatoApi()
        {
            Api = new ApiRequest();
        }
        private ApiRequest Api { get; set; }
        private static Logger Log = LogManager.GetCurrentClassLogger();

        public bool AddMovie(string imdbid, string apiKey, string title, Uri baseUrl, string profileId = default(string))
        {
            RestRequest request;
            request = string.IsNullOrEmpty(profileId) 
                ? new RestRequest {Resource = "/api/{apikey}/movie.add?title={title}&identifier={imdbid}"}
                : new RestRequest { Resource = "/api/{apikey}/movie.add?title={title}&identifier={imdbid}&profile_id={profileId}" };

            if (!string.IsNullOrEmpty(profileId))
            {
                request.AddUrlSegment("profileId", profileId);
            }

            request.AddUrlSegment("apikey", apiKey);
            request.AddUrlSegment("imdbid", imdbid);
            request.AddUrlSegment("title", title);

            var obj = Api.ExecuteJson<JObject>(request, baseUrl);
            Log.Trace("CP movie Add result count {0}", obj.Count);

            if (obj.Count > 0)
            {
                try
                {
                    Log.Trace("CP movie obj[\"success\"] = {0}", obj["success"]);
                    var result = (bool)obj["success"];
                    Log.Trace("CP movie Add result {0}", result);
                    return result;
                }
                catch (Exception e)
                {
                    Log.Fatal(e);
                    return false;
                }
            }
            return false;
        }

        /// <summary>
        /// Gets the status.
        /// </summary>
        /// <param name="url">The URL.</param>
        /// <param name="apiKey">The API key.</param>
        /// <returns></returns>
        public CouchPotatoStatus GetStatus(Uri url, string apiKey)
        {
            Log.Trace("Getting CP Status, ApiKey = {0}", apiKey);
            var request = new RestRequest
            {
                Resource = "api/{apikey}/app.available/",
                Method = Method.GET
            };

            request.AddUrlSegment("apikey", apiKey);

            return Api.Execute<CouchPotatoStatus>(request,url);
        }

        public CouchPotatoProfiles GetProfiles(Uri url, string apiKey)
        {
            Log.Trace("Getting CP Profiles, ApiKey = {0}", apiKey);
            var request = new RestRequest
            {
                Resource = "api/{apikey}/profile.list/",
                Method = Method.GET
            };

            request.AddUrlSegment("apikey", apiKey);

            return Api.Execute<CouchPotatoProfiles>(request, url);
        }
    }
}