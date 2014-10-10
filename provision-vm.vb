' Some OLD code used for provisioning servers from tempate in VB.net

Function VirtualCenterConnect(VirtualCenterName As String) As VimClient
    ' VC Variables
    Dim _client As New VimClient
    Dim _session As New UserSession
    Dim _vimhost As String = "https://" & VirtualCenterName & "/sdk"

    ' Placeholder Credentials
    Dim _vimuser As String = My.Settings.vCenterUsername
    Dim _vimpw As String = My.Settings.vCenterPassword

    ' Connect to Virtual Center
    Try
        _client.Connect(_vimhost)
        _session = _client.Login(_vimuser, _vimpw)
    Catch ex As Exception
        Return Nothing
    End Try

    Return _client
End Function


Function CreateVMShell(server As Server) As Boolean
    Dim _client As VimClient = VirtualCenterConnect(server.Item.Location.VirtualCenterServer)

    ' Server Variables
    Dim ServerName As String = <ServerName>
    Dim NetworkName As String = <NetworkName>
    Dim GuestId As String = <OSVMTypeString>
    Dim Annotation As String = "Auto Provisioned Server"
    
    ' Placement Parameters & VC Queries
    Dim ResourcePoolFilter As New NameValueCollection
    ResourcePoolFilter.Add("Name", ResourcePoolName)
    Dim ResourcePool As ResourcePool = _client.FindEntityView(GetType(ResourcePool), Nothing, ResourcePoolFilter, Nothing)

    Dim StoragePodFilter As New NameValueCollection
    StoragePodFilter.Add("Name", StoragePoolName)
    Dim StoragePod As StoragePod = _client.FindEntityView(GetType(StoragePod), Nothing, StoragePodFilter, Nothing)

    Dim FolderFilter As New NameValueCollection
    FolderFilter.Add("Name", FolderName)
    FolderFilter.Add("ChildType", "VirtualMachine")
    Dim Folder As Folder = _client.FindEntityView(GetType(Folder), Nothing, FolderFilter, Nothing)

    ' Create devices to attach to server
    Dim DeviceList As New List(Of VirtualDeviceConfigSpec)
    ' SCSI BUS
    DeviceList.Add(New VirtualDeviceConfigSpec With {.Operation = VirtualDeviceConfigSpecOperation.add,
                                                     .Device = New VirtualLsiLogicSASController With {.Key = 1, .BusNumber = 0, .SharedBus = VirtualSCSISharing.noSharing}})
    ' Network Device
    DeviceList.Add(New VirtualDeviceConfigSpec With {.Operation = VirtualDeviceConfigSpecOperation.add,
                                                         .Device = New VirtualVmxnet3 With {
                                                             .Key = -1,
                                                             .Backing = New VirtualEthernetCardNetworkBackingInfo With {
                                                                 .DeviceName = NetworkName,
                                                                 .Network = Nothing
                                                             },
                                                             .Connectable = New VirtualDeviceConnectInfo With {
                                                                 .Connected = True,
                                                                 .StartConnected = True,
                                                                 .AllowGuestControl = False}}})
    ' Disk Devices - add additional data disks
    ' Example here shows 50 and 20 GB disks
    Dim DiskList As New ArrayList
    DiskList.AddRange("50","20") 

    For DiskNumber = 0 To DiskList.Count - 1
        DeviceList.Add(New VirtualDeviceConfigSpec With {.Operation = VirtualDeviceConfigSpecOperation.add,
                                                          .FileOperation = VirtualDeviceConfigSpecFileOperation.create,
                                                          .Device = New VirtualDisk With {.Key = -100,
                                                                                          .Backing = New VirtualDiskFlatVer2BackingInfo With {
                                                                                              .DiskMode = "persistent",
                                                                                              .WriteThrough = False,
                                                                                              .ThinProvisioned = True,
                                                                                              .FileName = ""
                                                                                          },
                                                                                          .Connectable = New VirtualDeviceConnectInfo With {
                                                                                              .StartConnected = True,
                                                                                              .AllowGuestControl = False,
                                                                                              .Connected = True
                                                                                          },
                                                                                          .ControllerKey = 1,
                                                                                          .UnitNumber = DiskNumber,
                                                                                          .CapacityInKB = DiskList(DiskNumber) * 1024 * 1024}})
    Next

    ' Windows ISO Addition
    If server.Item.OperatingSystem.Name.Contains("Windows") Then
        Dim IsoFilter As New NameValueCollection
        IsoFilter.Add("Name", "ISODatastores")
        DeviceList.Add(New VirtualDeviceConfigSpec With {.Operation = VirtualDeviceConfigSpecOperation.add,
                                                            .Device = New VirtualCdrom With {
                                                                .Key = -1,
                                                                .ControllerKey = 200,
                                                                .Backing = New VirtualCdromIsoBackingInfo With {
                                                                    .FileName = server.Item.Location.WindowsISOLocation,
                                                                    .Datastore = _client.FindEntityView(GetType(Datastore), Nothing, IsoFilter, Nothing).MoRef
                                                                },
                                                                .Connectable = New VirtualDeviceConnectInfo With {
                                                                    .Connected = True,
                                                                    .StartConnected = True,
                                                                    .AllowGuestControl = True
                                                                }
                                                            }
                                                           })
    End If



    ' Get Storage DRS management interface
    Dim SRM As New StorageResourceManager(_client, _client.ServiceContent.StorageResourceManager)

    ' Construct the provisioning placement request
    Dim PSP As New StorageDrsPodSelectionSpec
    Dim IVC As New VmPodConfigForPlacement
    IVC.StoragePod = StoragePod.MoRef
    IVC.Disk = {New PodDiskLocator With {.DiskId = -100,
                .DiskBackingInfo = New VirtualDiskFlatVer2BackingInfo With {
                    .DiskMode = "persistent",
                    .WriteThrough = False,
                    .ThinProvisioned = True,
                    .FileName = ""
                    }
                    }}
    IVC.VmConfig = New StorageDrsVmConfigInfo With {.Enabled = True,
                                                    .Behavior = "automated",
                                                    .IntraVmAffinity = True}

    PSP.InitialVmConfig = {IVC}
    PSP.StoragePod = StoragePod.MoRef

    Dim CS As New VirtualMachineConfigSpec
    CS.Name = <ServerName>
    CS.GuestId = GuestId
    CS.Annotation = Annotation
    CS.Files = New VirtualMachineFileInfo With {.VmPathName = ""}
    CS.NumCPUs = <CPUCount>
    CS.MemoryMB = <MemoryGB> * 1024
    CS.DeviceChange = DeviceList.ToArray

    ' FIX for PE version 5 OSD builds on ESX 5.0. See KB 2060019.
    If server.Item.OperatingSystem.Name.Contains("Windows") Then
        CS.CpuFeatureMask = {New VirtualMachineCpuIdInfoSpec With {
         .Info = New HostCpuIdInfo With {
             .Edx = "----0---------------------------",
             .Level = -2147483647},
         .Operation = ArrayUpdateOperation.add}}
    End If

    Dim SP As New StoragePlacementSpec
    SP.Type = "create"
    SP.PodSelectionSpec = PSP
    SP.ConfigSpec = CS
    SP.ResourcePool = ResourcePool.MoRef
    SP.Folder = Folder.MoRef

    Dim placement As StoragePlacementResult
    Dim result As ApplyStorageRecommendationResult

    Try
        placement = SRM.RecommendDatastores(SP)
        result = SRM.ApplyStorageDrsRecommendation({placement.Recommendations(0).Key})
    Catch ex As Exception
        ' This will throw an exception when the recommendation cannot be accomidated OR the clone operation fails for any reason.
        logger.Error(ex.Message, ex)
        Return False
    End Try

    Dim ProvisionedVM As VirtualMachine
    Dim MacAddress As String
    Try
        ' Get resulting VM to save back hardware IDs to the database. Could slim down this call to just props we need.
        ProvisionedVM = _client.GetView(result.Vm, Nothing)
        MacAddress = CType(ProvisionedVM.Config.Hardware.Device.Where(Function(x) x.GetType() = GetType(VMware.Vim.VirtualVmxnet3)).FirstOrDefault, VirtualVmxnet3).MacAddress
    Catch ex As Exception
        logger.Error(ex.Message, ex)
        RaiseEvent ErrorAlertEngineer(server)
        Return False
    End Try

    ' Save updates to database.
    server.MACAddress = MacAddress
    server.HardwareID = ProvisionedVM.Config.Uuid
    server.VMID = ProvisionedVM.Config.InstanceUuid
    dbc.SaveChanges()

    _client.Disconnect()
End Function
