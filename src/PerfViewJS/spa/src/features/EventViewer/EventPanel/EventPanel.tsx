import {
  CheckboxVisibility,
  ConstrainMode,
  DetailsList,
  DetailsListLayoutMode,
  IColumn,
  IconButton,
  IDetailsColumnRenderTooltipProps,
  IDetailsHeaderProps,
  IRenderFunction,
  Modal,
  Panel,
  PanelType,
  PrimaryButton,
  ScrollablePane,
  ScrollbarVisibility,
  Sticky,
  StickyPositionType,
  TooltipHost,
} from "@fluentui/react";
import { useBoolean } from "@fluentui/react-hooks";
import React, { useEffect, useState } from "react";
import { useDataFileContext } from "context/DataFileContext";
import base64url from "base64url";
import { Hotspots } from "features/Hotspots";
import { cancelIcon, contentStyles as modalStyles, iconButtonStyles, wrapperStyle } from "./EventPanelStyles";
import { EventPanelColDef } from "./EventPanelColDef";
import { useRouteKeyContext } from "context/RouteContext";

interface IEventData {
  eventIndex: number;
  eventName: string;
  hasStack: boolean;
  processName: string;
  rest: string;
  timestamp: string;
}

interface IEventPanelProp {
  isOpen: boolean;
  dismissPanel: () => void;
  maxEventCount: number;
  start: string;
  end: string;
  textFilter: string;
  selectedEvents: string; //<-- csv
}

const GenerateRoute = (dataFile: string, eventData: IEventData[], eventIndex: number) => {
  const timestamp = eventData.find((ev) => ev.eventIndex === eventIndex)?.timestamp || "";
  return `${base64url.encode(
    JSON.stringify({
      a: dataFile,
      b: "-1",
      c: "-1",
      d: (parseFloat(timestamp) - 0.001).toFixed(3),
      e: (parseFloat(timestamp) + 0.001).toFixed(3),
      f: "",
      g: "",
      h: "",
      i: "",
      j: "",
      k: "",
      l: base64url.encode("Any Event"),
    }),
    "utf8"
  )}`;
};

const EventPanel: React.FC<IEventPanelProp> = (props) => {
  const { isOpen, maxEventCount, start, end, textFilter, selectedEvents, dismissPanel } = props;
  if (!isOpen) return <></>; //short circuit this component
  const { dataFile } = useDataFileContext();
  const { setRouteKey } = useRouteKeyContext();
  const [eventData, setEventData] = useState<IEventData[]>([]);
  const [isModalOpen, { setTrue: showModal, setFalse: hideModal }] = useBoolean(false);

  useEffect(() => {
    fetch(
      `/api/eventdata?filename=${dataFile}&maxEventCount=${maxEventCount}&start=${start}&end=${end}&filter=${base64url.encode(
        textFilter,
        "utf8"
      )}&eventTypes=${selectedEvents}`
    )
      .then((res) => res.json())
      .then((data) => {
        setEventData(data);
      });
  }, [dataFile, maxEventCount, start, end, textFilter, selectedEvents, dismissPanel]);

  const fetchHotSpots = (eventIndex: number) => {
    const encodedRoute = GenerateRoute(dataFile, eventData, eventIndex);
    setRouteKey(encodedRoute);
    showModal();
  };

  const renderFixedDetailsHeader: IRenderFunction<IDetailsHeaderProps> = (props, defaultRender) => {
    if (!props) {
      return null;
    }
    const onRenderColumnHeaderTooltip: IRenderFunction<IDetailsColumnRenderTooltipProps> = (tooltipHostProps) => (
      <TooltipHost {...tooltipHostProps} />
    );
    return (
      <Sticky stickyPosition={StickyPositionType.Header} isScrollSynced>
        {defaultRender?.({ ...props, onRenderColumnHeaderTooltip })}
      </Sticky>
    );
  };

  const renderItemColumn = (item?: IEventData, index?: number, column?: IColumn) => {
    if (column?.fieldName === "hasStack" && item?.hasStack) {
      return <PrimaryButton onClick={() => fetchHotSpots(item.eventIndex)}>View Stack</PrimaryButton>;
    } else {
      //? everything is optional..
      // eslint-disable-next-line @typescript-eslint/ban-ts-comment
      //@ts-ignore
      return item[column.fieldName];
    }
  };

  return (
    <Panel
      isOpen={isOpen}
      type={PanelType.smallFluid}
      closeButtonAriaLabel="Close"
      isHiddenOnDismiss={true}
      headerText="Events"
      onDismiss={dismissPanel}
    >
      <div style={wrapperStyle}>
        <ScrollablePane scrollbarVisibility={ScrollbarVisibility.auto}>
          <DetailsList
            setKey={"key"}
            checkboxVisibility={CheckboxVisibility.hidden}
            layoutMode={DetailsListLayoutMode.justified}
            constrainMode={ConstrainMode.unconstrained}
            compact={true}
            items={eventData}
            columns={EventPanelColDef}
            onRenderDetailsHeader={renderFixedDetailsHeader}
            onRenderItemColumn={renderItemColumn}
          ></DetailsList>
        </ScrollablePane>
      </div>
      <Modal isOpen={isModalOpen} onDismiss={hideModal} isBlocking={false}>
        <div className={modalStyles.header}>
          <span>Stack</span>
          <IconButton
            styles={iconButtonStyles}
            iconProps={cancelIcon}
            ariaLabel="Close popup modal"
            onClick={hideModal}
          />
        </div>
        <div className={modalStyles.body}>
          <Hotspots />
        </div>
      </Modal>
    </Panel>
  );
};

export { EventPanel };
