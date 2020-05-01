[<AutoOpen>]
module Farmer.Resources.ContainerGroups

open Farmer

type [<Measure>] Gb
type ContainerGroupOsType =
    | Windows
    | Linux
type ContainerGroupRestartPolicy =
    | Never
    | Always
    | OnFailure
type ContainerGroupIpAddressType =
    | PublicAddress
    | PrivateAddress
type ContainerProtocol = TCP | UDP
type ContainerPort =
    { Protocol : ContainerProtocol
      Port : uint16 }
type ContainerGroupIpAddress =
    { Type : ContainerGroupIpAddressType
      Ports : ContainerPort list }
/// Gigabytes
type ContainerResourceRequest =
    { Cpu : int
      Memory : float<Gb> }
type ContainerInstance =
    { Name : ResourceName
      Image : string
      Ports : uint16 list
      Resources : ContainerResourceRequest }


type ContainerGroup =
    { Name : ResourceName
      Location : Location
      ContainerInstances : ContainerInstance list
      OsType : ContainerGroupOsType
      RestartPolicy : ContainerGroupRestartPolicy
      IpAddress : ContainerGroupIpAddress }
    interface IResource with
        member this.ResourceName = this.Name
        member this.ToArmObject() =
            {| ``type`` = "Microsoft.ContainerInstance/containerGroups"
               apiVersion = "2018-10-01"
               name = this.Name.Value
               location = this.Location.ArmValue
               properties =
                   {| containers =
                       this.ContainerInstances
                       |> List.map (fun container ->
                           {| name = container.Name.Value.ToLowerInvariant ()
                              properties =
                               {| image = container.Image
                                  ports = container.Ports |> List.map (fun port -> {| port = port |})
                                  resources =
                                   {| requests =
                                       {| cpu = container.Resources.Cpu
                                          memoryInGb = container.Resources.Memory |}
                                   |}
                               |}
                           |})
                      osType =
                          match this.OsType with
                          | ContainerGroupOsType.Windows -> "Windows"
                          | ContainerGroupOsType.Linux -> "Linux"
                      restartPolicy =
                          match this.RestartPolicy with
                          | ContainerGroupRestartPolicy.Always -> "always"
                          | ContainerGroupRestartPolicy.Never -> "never"
                          | ContainerGroupRestartPolicy.OnFailure -> "onfailure"
                      ipAddress =
                       {| ``type`` =
                           match this.IpAddress.Type with
                           | ContainerGroupIpAddressType.PublicAddress -> "Public"
                           | ContainerGroupIpAddressType.PrivateAddress -> "Private"
                          ports = this.IpAddress.Ports
                          |> List.map (fun port ->
                           {| protocol = port.Protocol.ToString()
                              port = port.Port |})
                       |}
                   |}
            |} :> _


/// Represents configuration for a single Container.
type ContainerConfig =
    { /// The name of the container
      Name : ResourceName
      /// The container image
      Image : string
      /// List of ports the container listens on
      Ports : uint16 list
      /// Max number of CPU cores the container may use
      Cpu : int
      /// Max gigabytes of memory the container may use
      Memory : float<Gb>

      /// The name of the container group.
      ContainerGroupName : ResourceRef
      /// Container group OS.
      OsType : ContainerGroupOsType
      /// Restart policy for the container group.
      RestartPolicy : ContainerGroupRestartPolicy
      /// IP address for the container group.
      IpAddress : ContainerGroupIpAddress }

    member this.Key = buildKey this.Name
    /// Gets the ARM expression path to the key of this container group.
    member this.GroupKey = buildKey this.ContainerGroupName.ResourceName
    /// Gets the name of the container group.
    member this.GroupName = this.ContainerGroupName.ResourceName
    interface IResourceBuilder with
        member this.BuildResources location existingResources = [
            let container : ContainerInstance =
                { Name = this.Name
                  Image = this.Image
                  Ports = this.Ports
                  Resources =
                    { Cpu = this.Cpu
                      Memory = this.Memory } }
            match this.ContainerGroupName with
            | AutomaticallyCreated groupName ->
                NewResource
                    { ContainerGroup.Location = location
                      ContainerGroup.Name = groupName
                      ContainerGroup.ContainerInstances = [ container ]
                      ContainerGroup.OsType = this.OsType
                      ContainerGroup.RestartPolicy = this.RestartPolicy
                      ContainerGroup.IpAddress = this.IpAddress }
            | External resourceName ->
                existingResources
                |> List.choose(function :? ContainerGroup as cg -> Some cg | _ -> None)
                |> List.tryFind(fun g -> g.Name = resourceName)
                |> Option.map(fun group -> MergedResource(group, { group with ContainerInstances = group.ContainerInstances @ [ container ] }))
                |> Option.defaultValue (CouldNotLocate resourceName)
            | AutomaticPlaceholder ->
                NotSet
        ]

type ContainerBuilder() =
    member __.Yield _ =
      { Name = ResourceName.Empty
        Image = ""
        Ports = []
        Cpu = 1
        Memory = 1.5<Gb>
        ContainerGroupName = AutomaticPlaceholder
        OsType = ContainerGroupOsType.Linux
        RestartPolicy = ContainerGroupRestartPolicy.Always
        IpAddress = { Type = ContainerGroupIpAddressType.PublicAddress; Ports = [] } }
    member __.Run state =
        { state with
            ContainerGroupName =
                match state.ContainerGroupName.ResourceNameOpt with
                | Some _ -> state.ContainerGroupName
                | None -> AutomaticallyCreated (ResourceName (sprintf "%s-group" state.Name.Value))
        }
    /// Sets the name of the container instance
    [<CustomOperation "name">]
    member __.Name(state:ContainerConfig, name) = { state with Name = ResourceName name }
    /// Sets the container image.
    [<CustomOperation "image">]
    member __.Image (state:ContainerConfig, image) = { state with Image = image }
    /// Sets the ports the container exposes
    [<CustomOperation "ports">]
    member __.Ports (state:ContainerConfig, ports) = { state with Ports = ports }
    /// Sets the maximum CPU cores the container may use
    [<CustomOperationAttribute "cpu_cores">]
    member __.CpuCount (state:ContainerConfig, cpuCount) = { state with Cpu = cpuCount }
    /// Sets the maximum gigabytes of memory the container may use
    [<CustomOperationAttribute "memory">]
    member __.Memory (state:ContainerConfig, memory) = { state with Memory = memory }
    [<CustomOperation "group_name">]
    /// Sets the name of the container group.
    member __.GroupName(state:ContainerConfig, name) = { state with ContainerGroupName = AutomaticallyCreated name }
    member this.GroupName(state:ContainerConfig, name) = this.GroupName(state, ResourceName name)
    /// Links this container to an already-created container group.
    [<CustomOperation "link_to_container_group">]
    member __.LinkToGroup(state:ContainerConfig, group:ContainerConfig) = { state with ContainerGroupName = External group.GroupName }
    /// Sets the OS type (default Linux)
    [<CustomOperation "os_type">]
    member __.OsType(state:ContainerConfig, osType) = { state with OsType = osType }
    /// Sets the restart policy (default Always)
    [<CustomOperation "restart_policy">]
    member __.RestartPolicy(state:ContainerConfig, restartPolicy) = { state with RestartPolicy = restartPolicy }
    /// Sets the IP addresss (default Public)
    [<CustomOperation "ip_address">]
    member __.IpAddress(state:ContainerConfig, addressType, ports) = { state with IpAddress = { Type = addressType; Ports = ports |> Seq.map(fun (prot, port) -> { ContainerPort.Protocol = prot; ContainerPort.Port = port }) |> Seq.toList } }
    /// Adds a TCP port to be externally accessible
    [<CustomOperation "add_tcp_port">]
    member __.AddTcpPort(state:ContainerConfig, port) = { state with IpAddress = { state.IpAddress with Ports = { Protocol= TCP; Port = port } :: state.IpAddress.Ports } }
    /// Adds a UDP port to be externally accessible
    [<CustomOperation "add_udp_port">]
    member __.AddUdpPort(state:ContainerConfig, port) = { state with IpAddress = { state.IpAddress with Ports = { Protocol= UDP; Port = port } :: state.IpAddress.Ports } }
let container = ContainerBuilder()