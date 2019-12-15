﻿using Nethereum.ABI.Encoders;
using Nethereum.Signer;
using Nethereum.Util;
using RecentLib.Models;
using RecentLib.Models.Blockchain;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using static RecentLib.Constants.RecentProject;

namespace RecentLib
{

    public partial class RecentCore
    {




        /// <summary>
        /// Returns Relayer
        /// </summary>
        /// <param name="relayerId">The relayerId</param>
        /// <returns>Relayer</returns>
        public async Task<Relayer> getRelayer(string owner, bool includeBalance = false, string balanceAddress = "")
        {
            var contract = _web3.Eth.GetContract(PaymentChannelsABI, PaymentChannelsContract);
            var function = contract.GetFunction("relayers");
            var result = await function.CallDeserializingToObjectAsync<RelayerData>(owner);
            uint? lockUntilBlock = null;
            decimal? balance = null;
            if (includeBalance)
            {
                var userBalanceFunction = contract.GetFunction("userDepositOnRelayer");
                var userBalance = await userBalanceFunction.CallDeserializingToObjectAsync<DepositOnRelayerData>(string.IsNullOrEmpty(balanceAddress) ? _wallet.address : balanceAddress, owner);
                lockUntilBlock = userBalance.lockUntilBlock;
                balance = weiToRecent(userBalance.balance);
            }
            return new Relayer
            {
                domain = result.domain,
                fee = result.fee / 10m,
                name = result.name,
                owner = result.owner,
                lockUntilBlock = lockUntilBlock,
                userBalance = balance,
                epoch = result.epoch,
                maxCoins = result.maxCoins,
                maxTxThroughput = result.maxTxThroughput,
                maxUsers = result.maxUsers,
                offchainTxDelay = result.offchainTxDelay

            };
        }


        public async Task<uint> getCurrentEpoch()
        {
            var contract = _web3.Eth.GetContract(PaymentChannelsABI, PaymentChannelsContract);
            var function = contract.GetFunction("getCurrentEpoch");
            var currentEpoch = await function.CallAsync<BigInteger>();
            return (uint)currentEpoch;
        }


        public async Task<string> getEpochRelayerOwnerByIndex(uint epoch, uint index)
        {
            var contract = _web3.Eth.GetContract(PaymentChannelsABI, PaymentChannelsContract);
            var function = contract.GetFunction("epochRelayerOwnerByIndex");
            var owner = await function.CallAsync<string>(epoch, index);
            return owner;
        }

        /// <summary>
        /// Get registered Relayers
        /// </summary>
        /// <returns>The list of Relayers</returns>
        public async Task<List<Relayer>> getRelayers(uint? epoch)
        {
            var contract = _web3.Eth.GetContract(PaymentChannelsABI, PaymentChannelsContract);
            var function = contract.GetFunction("relayersCounter");
            if (!epoch.HasValue)
            {
                epoch = await getCurrentEpoch();
            }
            uint totalRelayersCount = (uint)await function.CallAsync<BigInteger>(epoch);

            var ret = new List<Relayer>();
            Parallel.For(0, totalRelayersCount, i =>
            {
                var owner = getEpochRelayerOwnerByIndex(epoch.Value, (uint)i);
                ret.Add(getRelayer(owner.Result).Result);

            });
            return ret;
        }

        



        /// <summary>
        /// Deposit to Relayer
        /// </summary>
        /// <param name="id">The Relayer Id</param>
        /// <param name="amount">The amount</param>
        /// <param name="lockTimeInDays">Time lock perdio in days</param>
        /// <returns>The tx</returns>
        public async Task<OutgoingTransaction> depositToRelayer(string owner, decimal amount, uint lockUntilBlock, bool calcNetFeeOnly, bool waitReceipt, CancellationTokenSource cancellationToken)
        {
            if (lockUntilBlock > await getLastBlock())
                throw new Exception("lockUntilBlock should be greater than current block");
            return await executePaymentChannelsMethod("depositToRelayer", new object[] { owner, lockUntilBlock }, calcNetFeeOnly, waitReceipt, cancellationToken, amount);
        }

        /// <summary>
        /// Withdraw funds from Relayer
        /// </summary>
        /// <param name="domain">The Relayer domain or Ip</param>
        /// <param name="amount">The amount</param>
        /// <returns>The tx</returns>
        public async Task<OutgoingTransaction> withdrawFunds(string owner, decimal amount, bool calcNetFeeOnly, bool waitReceipt, CancellationTokenSource cancellationToken)
        {
            return await withdrawFunds(owner, amount, calcNetFeeOnly, waitReceipt, cancellationToken);
        }

        public async Task<SignedOffchainTransaction> signOffchainPayment(SignedOffchainTransaction offchainTransaction)
        {
            var signer = new MessageSigner();
            var encoder = new Nethereum.ABI.ABIEncode();
            var payloadEncoded = encoder.GetABIEncodedPacked(new object[] { offchainTransaction.relayerId, offchainTransaction.beneficiary, offchainTransaction.nonce, offchainTransaction.amount, offchainTransaction.fee });
            var proof = Nethereum.Util.Sha3Keccack.Current.CalculateHash(payloadEncoded);
            var payloadEncodedWithPrefix = Nethereum.Util.Sha3Keccack.Current.CalculateHash(encoder.GetABIEncodedPacked(new object[] { prefix, proof }));
            var fromcontract = await getFinalizeOffchainRelayerSignature(offchainTransaction);

            var byte32decoder = new Nethereum.ABI.Decoders.Bytes32TypeDecoder();
            var our = byte32decoder.Decode(payloadEncodedWithPrefix,typeof(string));
            var their = byte32decoder.Decode(fromcontract, typeof(string));

            //offchainTransaction.h = fromcontract; This works
            offchainTransaction.h = payloadEncodedWithPrefix;

            var signedTx = signer.Sign(offchainTransaction.h, _wallet.PK);

            //var byte32encoder = new Nethereum.ABI.Encoders.Bytes32TypeEncoder();
            //offchainTransaction.h = byte32encoder.Encode(signedTx);


            var signature = EthereumMessageSigner.ExtractEcdsaSignature(signedTx);
            offchainTransaction.v = signature.V.FirstOrDefault();
            offchainTransaction.r = signature.R;
            offchainTransaction.s = signature.S;
            offchainTransaction.signer = _wallet.address;
            return offchainTransaction;
        }

        public async Task<bool> checkFinalizeOffchainRelayerSignature(SignedOffchainTransaction signedOffchainTransaction)
        {
            var contract = _web3.Eth.GetContract(PaymentChannelsABI, PaymentChannelsContract);
            var function = contract.GetFunction("checkFinalizeOffchainRelayerSignature");
            string signer = await function.CallAsync<string>(signedOffchainTransaction.h, signedOffchainTransaction.v, signedOffchainTransaction.r, signedOffchainTransaction.s, signedOffchainTransaction.relayerId, signedOffchainTransaction.nonce, signedOffchainTransaction.fee, signedOffchainTransaction.beneficiary, signedOffchainTransaction.amount);
            return AddressUtil.Current.ConvertToChecksumAddress(signer) == AddressUtil.Current.ConvertToChecksumAddress(signedOffchainTransaction.signer);
        }

        public async Task<byte[]> getFinalizeOffchainRelayerSignature(SignedOffchainTransaction signedOffchainTransaction)
        {
            var contract = _web3.Eth.GetContract(PaymentChannelsABI, PaymentChannelsContract);
            var function = contract.GetFunction("getFinalizeOffchainRelayerSignature");
            return await function.CallAsync <byte[]> (signedOffchainTransaction.relayerId, signedOffchainTransaction.nonce, signedOffchainTransaction.fee, signedOffchainTransaction.beneficiary, signedOffchainTransaction.amount);
        }

        protected async Task<OutgoingTransaction> executePaymentChannelsMethod(string method, object[] input, bool calcNetFeeOnly, bool waitReceipt, CancellationTokenSource cancellationToken, decimal? value = null)
        {

            var contract = _web3.Eth.GetContract(PaymentChannelsABI, PaymentChannelsContract);
            var function = contract.GetFunction(method);
            
            return await executeBlockchainTransaction(_wallet.address, input, calcNetFeeOnly, function, waitReceipt, cancellationToken, recentToWei(value));
        }

    }
}
