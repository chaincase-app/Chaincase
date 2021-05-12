﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reactive.Linq;
using System.Threading.Tasks;
using Chaincase.Common;
using Chaincase.Common.Contracts;
using Chaincase.Common.Services;
using ReactiveUI;
using WalletWasabi.Blockchain.Keys;
using WalletWasabi.Helpers;
using WalletWasabi.Wallets;

namespace Chaincase.UI.ViewModels
{
    public class BackUpViewModel : ReactiveObject
    {
        private readonly Config _config;
        private readonly UiConfig _uiConfig;
        private readonly IHsmStorage _hsm;
        private readonly SensitiveStorage _storage;
        private readonly WalletManager _walletManager;
        private List<string> _seedWords;

        public bool HasNoSeedWords => !_uiConfig.HasSeed && !_uiConfig.HasIntermediateKey;

        private string LegacyWordsLoc => $"{_config.Network}-seedWords";

        public BackUpViewModel(Config config, UiConfig uiConfig, IHsmStorage hsm, SensitiveStorage storage, ChaincaseWalletManager walletManager)
        {
            _config = config;
            _uiConfig = uiConfig;
            _hsm = hsm;
            _storage = storage;
            _walletManager = walletManager;
        }

        public async Task InitSeedWords(string password)
        {
            string wordString = null;
            try
            {
                await Task.Run(async () =>
                {
                    // this next line doesn't really run async :/
                    wordString = await _storage.GetSeedWords(password);
                    if (wordString is null)
                        throw new KeyNotFoundException();
                });
            }
            catch (ArgumentException e)
            {
                // bad password & thus bad key derived
                throw e;
            }
            catch (KeyNotFoundException)
            {
                // try migrate from the legacy system
                wordString = await SetAlphaToBetaSeedWords(password);
            }
            finally
            {
                SeedWords = wordString?.Split(' ').ToList();
            }
        }

        public async Task<string> SetAlphaToBetaSeedWords(string password)
        {
            PasswordHelper.Guard(password);
            string walletFilePath = Path.Combine(_walletManager.WalletDirectories.WalletsDir, $"{_config.Network}.json");
            // the old one will ask you for 
            var seedWords = await _hsm.GetAsync(LegacyWordsLoc);
            if (string.IsNullOrEmpty(seedWords))
            {
                throw new Exception("No seed words");
            }

            await Task.Run(() => KeyManager.FromFile(walletFilePath).GetMasterExtKey(password ?? ""));
            await _storage.SetSeedWords(password, seedWords);
            return seedWords;
        }

        public void SetIsBackedUp()
        {
            _uiConfig.IsBackedUp = true;
            _uiConfig.ToFile(); // successfully backed up!
        }

        public List<string> SeedWords
        {
            get => _seedWords;
            set => this.RaiseAndSetIfChanged(ref _seedWords, value);
        }
    }
}
