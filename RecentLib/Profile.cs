﻿using RecentLib.Models;
using static RecentLib.Constants.RecentProject;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using RecentLib.Models.Blockchain;
using System.Threading;
using Ipfs.Http;
using System.IO;

namespace RecentLib
{
    public partial class RecentCore
    {


        /// <summary>
        /// Uploads a binary file to Ipfs
        /// </summary>
        /// <param name="binary">The byte array</param>
        /// <returns>The Ipfs CID</returns>
        public async Task<string> uploadBinary(byte[] binary)
        {
            var ipfs = new IpfsClient(IpfsClientEndpoint);

            var ret = await ipfs.FileSystem.AddAsync(new MemoryStream(binary));
            return ret.Id.Hash.ToString();
        }

        /// <summary>
        /// Downloads a binary file to Ipfs from ipfs
        /// </summary>
        /// <param name="cid">The Ipfs CID</param>
        /// <returns>The byte array</returns>
        public async Task<byte[]> downloadBinary(string cid)
        {
            var ipfs = new IpfsClient(IpfsClientEndpoint);
            var stream = await ipfs.FileSystem.ReadFileAsync(cid);
            using (MemoryStream ms = new MemoryStream())
            {
                stream.CopyTo(ms);
                return ms.ToArray();
            }
        }

        /// <summary>
        /// Get a URL for uploaded binary
        /// </summary>
        /// <param name="cid">The Ipfs CID</param>
        /// <returns>The Url</returns>
        public string getIpfsCIDUrl(string cid)
        {
            return $"{IpfsGatewayEndpoint}{cid}";
        }



        /// <summary>
        /// Returns user profile data
        /// </summary>
        /// <param name="address">User address</param>
        /// <returns></returns>
        public async Task<UserProfile> getUserProfileData(string address)
        {
            var contract = _web3.Eth.GetContract(UserProfileABI,ProfileContract);
            var function = contract.GetFunction("users");
            var result =await function.CallDeserializingToObjectAsync<UserProfileData>(address);
            return new UserProfile
            {
                avatarIpfsCID = result.avatarIpfsCID,
                contentConsumerRating = result.contentConsumerVotes>0 ? result.contentConsumerRatingTotalPoints / 100m / result.contentConsumerVotes : (decimal?)null,
                contentProviderRating = result.contentProviderVotes > 0 ? result.contentProviderRatingTotalPoints / 100m / result.contentProviderVotes : (decimal?)null,
                contentConsumerVotes = result.contentConsumerVotes,
                contentProviderVotes = result.contentProviderVotes,
                disabled = result.disabled,
                firstname = result.firstname,
                lastname = result.lastname,
                nickname = result.nickname,
                statusText =result.statusText
            };
        }

        /// <summary>
        /// Returns user rating data
        /// </summary>
        /// <param name="address">User address</param>
        /// <returns></returns>
        public async Task<UserRating> getUserRating(string address)
        {
            var contract = _web3.Eth.GetContract(UserProfileABI, ProfileContract);
            var function = contract.GetFunction("getUserRating");
            var result = await function.CallDeserializingToObjectAsync<UserRatingData>(address);
            return new UserRating
            {
                contentConsumerRating = result.consumerRating > 0 ? result.consumerRating / 100m : (decimal?)null,
                contentProviderRating = result.providerRating > 0 ? result.providerRating / 100m : (decimal?)null
            };
        }




        /// <summary>
        /// Updates user profile data
        /// </summary>
        /// <param name="userProfile">User profile</param>
        /// <returns></returns>
        public async Task<OutgoingTransaction> setUserProfileData(UserProfile userProfile, bool calcNetFeeOnly, bool waitReceipt, CancellationTokenSource cancellationToken)
        {
            return await executeProfileMethod("updateProfile", new object[] { userProfile.nickname, userProfile.avatarIpfsCID, userProfile.firstname, userProfile.lastname, userProfile.statusText, userProfile.disabled }, calcNetFeeOnly, waitReceipt, cancellationToken);
        }

        /// <summary>
        /// Rate user as Content Provider
        /// </summary>
        /// <param name="userProfile">User profile</param>
        /// <returns></returns>
        public async Task<OutgoingTransaction> rateProvider(string address, decimal rating, bool calcNetFeeOnly, bool waitReceipt, CancellationTokenSource cancellationToken)
        {
            return await executeProfileMethod("rateProvider", new object[] { _wallet.address, (uint)(rating * 100) }, calcNetFeeOnly, waitReceipt, cancellationToken);
        }

        /// <summary>
        /// Rate user as Content Consumer
        /// </summary>
        /// <param name="userProfile">User profile</param>
        /// <returns></returns>
        public async Task<OutgoingTransaction> rateConsumer(string address, decimal rating, bool calcNetFeeOnly, bool waitReceipt, CancellationTokenSource cancellationToken)
        {
            return await executeProfileMethod("rateConsumer", new object[] { address, (uint)(rating * 100) }, calcNetFeeOnly, waitReceipt, cancellationToken);
        }

        protected async Task<OutgoingTransaction> executeProfileMethod(string method, object[] input, bool calcNetFeeOnly, bool waitReceipt, CancellationTokenSource cancellationToken)
        {

            var contract = _web3.Eth.GetContract(UserProfileABI, ProfileContract);
            var function = contract.GetFunction(method);

            return await executeBlockchainTransaction(_wallet.address, input, calcNetFeeOnly, function, waitReceipt, cancellationToken);
        }

    }
}
