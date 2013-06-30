﻿/*
 * Copyright (C) 2013 Colin Mackie.
 * This software is distributed under the terms of the GNU General Public License.
 *
 * This program is free software: you can redistribute it and/or modify
 * it under the terms of the GNU General Public License as published by
 * the Free Software Foundation, either version 3 of the License, or
 * (at your option) any later version.
 *
 * This program is distributed in the hope that it will be useful,
 * but WITHOUT ANY WARRANTY; without even the implied warranty of
 * MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
 * GNU General Public License for more details.
 *
 * You should have received a copy of the GNU General Public License
 * along with this program.  If not, see <http://www.gnu.org/licenses/>.
 */

using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Security.Cryptography;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Serialization;
using System.Xml.XPath;

using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Crypto.Engines;
using Org.BouncyCastle.Crypto.Macs;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Crypto.Paddings;
using Org.BouncyCastle.Crypto.Digests;
using Org.BouncyCastle.Crypto.Generators;

#if NUNIT
using NUnit.Framework;
#endif

#if NETCF
using OpenNETCF.Security.Cryptography;
#endif

namespace WinAuth
{
	/// <summary>
	/// Class that implements Battle.net Mobile Authenticator v1.1.0.
	/// </summary>
	public class TrionAuthenticator : Authenticator
	{
		/// <summary>
		/// Size of model string
		/// </summary>
		private const int MODEL_SIZE = 15;

		/// <summary>
		/// String of possible chars we use in our random model string
		/// </summary>
		private const string MODEL_CHARS = "1234567890ABCDEF";

		/// <summary>
		/// Number of digits in code
		/// </summary>
		private const int CODE_DIGITS = 8;

		/// <summary>
		/// URLs for all mobile services
		/// </summary>
		private static string ENROLL_URL = "https://rift.trionworlds.com/external/create-device-key";
		private static string SYNC_URL = "https://auth.trionworlds.com/time";
		private static string SECURITYQUESTIONS_URL = "https://rift.trionworlds.com/external/get-account-security-questions.action";
		private static string RESTORE_URL = "https://rift.trionworlds.com/external/retrieve-device-key.action";

		#region Authenticator data

		/// <summary>
		/// Get/set the combined secret data value
		/// </summary>
		public override string SecretData
		{
			get
			{
				// for Battle.net, this is the key + serial
				return Authenticator.ByteArrayToString(SecretKey) + Authenticator.ByteArrayToString(Encoding.UTF8.GetBytes(Serial));
			}
			set
			{
				// for Battle.net, extract key + serial
				if (string.IsNullOrEmpty(value) == false)
				{
					SecretKey = Authenticator.StringToByteArray(value.Substring(0, 40));
					Serial = Encoding.UTF8.GetString(Authenticator.StringToByteArray(value.Substring(40)));
				}
				else
				{
					SecretKey = null;
					Serial = null;
				}
			}
		}

		public string DeviceId {get; set;}

		#endregion

		/// <summary>
		/// Create a new Authenticator object
		/// </summary>
		public TrionAuthenticator()
			: base(CODE_DIGITS)
		{
		}

		/// <summary>
		/// Enroll the authenticator with the server.
		/// </summary>
		public void Enroll()
		{
			// generate model name
			string deviceId = GeneralRandomModel();

			string postdata = "deviceId=" + deviceId;

			// call the enroll server
			HttpWebRequest request = (HttpWebRequest)WebRequest.Create(ENROLL_URL);
			request.Method = "POST";
			request.ContentType = "application/x-www-form-urlencoded";
			request.ContentLength = postdata.Length;
			StreamWriter requestStream = new StreamWriter(request.GetRequestStream());
			requestStream.Write(postdata);
			requestStream.Close();
			string responseData;
			using (HttpWebResponse response = (HttpWebResponse)request.GetResponse())
			{
				// OK?
				if (response.StatusCode != HttpStatusCode.OK)
				{
					throw new InvalidEnrollResponseException(string.Format("{0}: {1}", (int)response.StatusCode, response.StatusDescription));
				}

				// load the response
				using (StreamReader responseStream = new StreamReader(response.GetResponseStream()))
				{
					responseData = responseStream.ReadToEnd();
				} 
			}

			// return data:
			// <DeviceKey>
			//	<DeviceId />
			//	<SerialKey />
			//	<SecretKey />
			//	<ErrorCode /> only exists if an error
			// </DeviceKey>
			XmlDocument doc = new XmlDocument();
			doc.LoadXml(responseData);
			XmlNode node = doc.SelectSingleNode("//ErrorCode");
			if (node != null && string.IsNullOrEmpty(node.InnerText) == false)
			{
				// an error occured
				throw new InvalidEnrollResponseException(node.InnerText);
			}

			// get the secret key
			SecretKey = Encoding.UTF8.GetBytes(doc.SelectSingleNode("//SecretKey").InnerText);

			// get the serial number
			string serial = doc.SelectSingleNode("//SerialKey").InnerText;
			Serial = Regex.Replace(serial, @"(.{4})", "$1 ").Trim().Replace(" ", "-");

			// save the device
			DeviceId = doc.SelectSingleNode("//DeviceId").InnerText;
		}

#if DEBUG
		/// <summary>
		/// Debug version of enroll that just returns a known test authenticator
		/// </summary>
		/// <param name="testmode"></param>
		public void Enroll(bool testmode)
		{
			if (!testmode)
			{
				Enroll();
			}
			else
			{
				//string responseData = "<DeviceKey><DeviceId>zarTM0v5ko0BwrOYQV1HhsE4Q0stqgbF</DeviceId><SerialKey>FJP7H9DG3T67</SerialKey><SecretKey>DP7FFJZKLG6ZNCJTNNMT</SecretKey></DeviceKey>";
				string responseData = "<DeviceKey><DeviceId>19897B57952648559364352F7FE9B8A8</DeviceId><SerialKey>HM3ZMQ233FPZ</SerialKey><SecretKey>6MYNGRGYX7XZQNL6T2M6</SecretKey></DeviceKey>";

				//	<DeviceId />
				//	<SerialKey />
				//	<SecretKey />
				//	<ErrorCode /> only exists if an error
				// </DeviceKey>
				XmlDocument doc = new XmlDocument();
				doc.LoadXml(responseData);
				XmlNode node = doc.SelectSingleNode("//ErrorCode");
				if (node != null && string.IsNullOrEmpty(node.InnerText) == false)
				{
					// an error occured
					throw new InvalidEnrollResponseException(node.InnerText);
				}

				// get the secret key
				SecretKey = Encoding.UTF8.GetBytes(doc.SelectSingleNode("//SecretKey").InnerText);

				// get the serial number
				string serial = doc.SelectSingleNode("//SerialKey").InnerText;
				Serial = Regex.Replace(serial, @"(.{4})", "$1 ").Trim().Replace(" ", "-");

				DeviceId = doc.SelectSingleNode("//DeviceId").InnerText;
			}
		}
#endif


		/// <summary>
		/// Synchorise this authenticator's time with server time. We update our data record with the difference from our UTC time.
		/// </summary>
		public override void Sync()
		{
			// create a connection to time sync server
			HttpWebRequest request = (HttpWebRequest)WebRequest.Create(SYNC_URL);
			request.Method = "GET";

			// get response
			string responseData;
			using (HttpWebResponse response = (HttpWebResponse)request.GetResponse())
			{
				// OK?
				if (response.StatusCode != HttpStatusCode.OK)
				{
					throw new ApplicationException(string.Format("{0}: {1}", (int)response.StatusCode, response.StatusDescription));
				}

				// load the response
				using (StreamReader responseStream = new StreamReader(response.GetResponseStream()))
				{
					responseData = responseStream.ReadToEnd();
				}
			}

			// return data is string version of time in milliseconds since epoch

			// get the difference between the server time and our current time
			long serverTimeDiff = long.Parse(responseData) - CurrentTime;

			// update the Data object
			ServerTimeDiff = serverTimeDiff;
		}

		/// <summary>
		/// Get the secret questions for an account
		/// </summary>
		/// <param name="email">user's account email</param>
		/// <param name="password">user's account password</param>
		/// <param name="question1">returned secret question 1</param>
		/// <param name="question2">returned secret question 2</param>
		public static void SecurityQuestions(string email, string password, out string question1, out string question2)
		{
			string postdata = "emailAddress=" + email + "&password=" + password;

			// call the enroll server
			HttpWebRequest request = (HttpWebRequest)WebRequest.Create(SECURITYQUESTIONS_URL);
			request.Method = "POST";
			request.ContentType = "application/x-www-form-urlencoded";
			request.ContentLength = postdata.Length;
			StreamWriter requestStream = new StreamWriter(request.GetRequestStream());
			requestStream.Write(postdata);
			requestStream.Close();
			string responseData;
			using (HttpWebResponse response = (HttpWebResponse)request.GetResponse())
			{
				// OK?
				if (response.StatusCode != HttpStatusCode.OK)
				{
					throw new InvalidRestoreResponseException(string.Format("{0}: {1}", (int)response.StatusCode, response.StatusDescription));
				}

				// load the response
				using (StreamReader responseStream = new StreamReader(response.GetResponseStream()))
				{
					responseData = responseStream.ReadToEnd();
				}
			}

			// return data:
			// <SecurityQuestions>
			//	<EmailAddress />
			//	<FirstQuestion />
			//	<SecondQuestion />
			//	<ErrorCode /> only exists if an error
			// </SecurityQuestions>
			XmlDocument doc = new XmlDocument();
			doc.LoadXml(responseData);
			XmlNode node = doc.SelectSingleNode("//ErrorCode");
			if (node != null && string.IsNullOrEmpty(node.InnerText) == false)
			{
				// an error occured
				throw new InvalidRestoreResponseException(node.InnerText);
			}

			// get the questions
			question1 = doc.SelectSingleNode("//FirstQuestion").InnerText;
			question2 = doc.SelectSingleNode("//SecondQuestion").InnerText;
		}

		/// <summary>
		/// Restore an authenticator using the account details and security questions
		/// </summary>
		/// <param name="email">user's account email</param>
		/// <param name="password">user's account password</param>
		/// <param name="deviceId">register authenticator deviceid</param>
		/// <param name="answer1">answer to secret question 1</param>
		/// <param name="answer2">answer to secret question 2</param>
		public void Restore(string email, string password, string deviceId, string answer1, string answer2)
		{
			string postdata = "emailAddress=" + email
				+ "&password=" + password
				+ "&deviceId=" + deviceId
				+ "&securityAnswer=" + answer1
				+ "&secondSecurityAnswer=" + answer2;

			// call the enroll server
			HttpWebRequest request = (HttpWebRequest)WebRequest.Create(RESTORE_URL);
			request.Method = "POST";
			request.ContentType = "application/x-www-form-urlencoded";
			request.ContentLength = postdata.Length;
			StreamWriter requestStream = new StreamWriter(request.GetRequestStream());
			requestStream.Write(postdata);
			requestStream.Close();
			string responseData;
			using (HttpWebResponse response = (HttpWebResponse)request.GetResponse())
			{
				// OK?
				if (response.StatusCode != HttpStatusCode.OK)
				{
					throw new InvalidRestoreResponseException(string.Format("{0}: {1}", (int)response.StatusCode, response.StatusDescription));
				}

				// load the response
				using (StreamReader responseStream = new StreamReader(response.GetResponseStream()))
				{
					responseData = responseStream.ReadToEnd();
				}
			}

			// return data:
			// <DeviceKey>
			//	<DeviceId />
			//	<SerialKey />
			//	<SecretKey />
			//	<ErrorCode /> only exists if an error
			// </DeviceKey>
			XmlDocument doc = new XmlDocument();
			doc.LoadXml(responseData);
			XmlNode node = doc.SelectSingleNode("//ErrorCode");
			if (node != null && string.IsNullOrEmpty(node.InnerText) == false)
			{
				// an error occured
				throw new InvalidRestoreResponseException(node.InnerText);
			}

			// get the secret key
			SecretKey = Encoding.UTF8.GetBytes(doc.SelectSingleNode("//SecretKey").InnerText);

			// get the serial number
			string serial = doc.SelectSingleNode("//SerialKey").InnerText;
			Serial = Regex.Replace(serial, @"(.{4})", "$1 ").Trim().Replace(" ", "-");

			// save the device
			DeviceId = doc.SelectSingleNode("//DeviceId").InnerText;
		}

		/// <summary>
		/// Create a random Model string for initialization to armor the init string sent over the wire
		/// </summary>
		/// <returns>Random model string</returns>
		private static string GeneralRandomModel()
		{
			// seed a new RNG
			RNGCryptoServiceProvider randomSeedGenerator = new RNGCryptoServiceProvider();
			byte[] seedBuffer = new byte[4];
			randomSeedGenerator.GetBytes(seedBuffer);
			Random random = new Random(BitConverter.ToInt32(seedBuffer, 0));

			// create a model string with available characters
			StringBuilder model = new StringBuilder(MODEL_SIZE);
			for (int i = MODEL_SIZE; i > 0; i--)
			{
				model.Append(MODEL_CHARS[random.Next(MODEL_CHARS.Length)]);
			}

			return model.ToString();
		}

	}

}