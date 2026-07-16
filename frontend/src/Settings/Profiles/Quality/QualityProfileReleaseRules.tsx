/* eslint-disable react/jsx-no-bind */
/* eslint-disable no-nested-ternary */
import React, { useCallback } from 'react';
import FormInputGroup from 'Components/Form/FormInputGroup';
import FormLabel from 'Components/Form/FormLabel';
import Icon from 'Components/Icon';
import Button from 'Components/Link/Button';
import { icons, inputTypes, kinds, sizes } from 'Helpers/Props';
import { useSortedCustomFormats } from 'Settings/CustomFormats/CustomFormats/useCustomFormats';
import {
  ReleaseRule,
  ReleaseRuleConditionOperator,
  ReleaseRuleConditionType,
} from './useQualityProfiles';

interface QualityProfileReleaseRulesProps {
  rules: ReleaseRule[];
  onChange: (rules: ReleaseRule[]) => void;
}

const CONDITION_TYPES = [
  { key: ReleaseRuleConditionType.ReleaseGroup, value: 'Release Group' },
  { key: ReleaseRuleConditionType.AudioType, value: 'Audio Type' },
  { key: ReleaseRuleConditionType.CustomFormat, value: 'Custom Format' },
  { key: ReleaseRuleConditionType.Quality, value: 'Quality' },
  { key: ReleaseRuleConditionType.ReleaseTitle, value: 'Release Title' },
];

const OPERATORS = [
  { key: ReleaseRuleConditionOperator.Exact, value: 'is exactly' },
  { key: ReleaseRuleConditionOperator.Contains, value: 'contains' },
];

const AUDIO_OPTIONS = [
  { key: 'Any', value: 'Any' },
  { key: 'English', value: 'English Only' },
  { key: 'Japanese', value: 'Japanese Only' },
  { key: 'Dual Audio', value: 'Dual Audio' },
  { key: 'Multi Audio', value: 'Multi Audio' },
];

export default function QualityProfileReleaseRules({
  rules,
  onChange,
}: QualityProfileReleaseRulesProps) {
  const { data: customFormats } = useSortedCustomFormats();
  const customFormatOptions = React.useMemo(
    () =>
      customFormats.map((cf) => ({ key: cf.id.toString(), value: cf.name })),
    [customFormats]
  );

  const handleAddRule = useCallback(() => {
    onChange([
      ...rules,
      {
        name: `Rule ${rules.length + 1}`,
        conditions: [
          {
            conditionType: ReleaseRuleConditionType.ReleaseGroup,
            operator: ReleaseRuleConditionOperator.Exact,
            value: '',
          },
        ],
      },
    ]);
  }, [rules, onChange]);

  const handleUpdateRule = useCallback(
    (index: number, newRule: ReleaseRule) => {
      const newRules = [...rules];
      newRules[index] = newRule;
      onChange(newRules);
    },
    [rules, onChange]
  );

  const handleRemoveRule = useCallback(
    (index: number) => {
      const newRules = [...rules];
      newRules.splice(index, 1);
      onChange(newRules);
    },
    [rules, onChange]
  );

  const handleMoveRule = useCallback(
    (index: number, direction: -1 | 1) => {
      if (index + direction < 0 || index + direction >= rules.length) return;
      const newRules = [...rules];
      const temp = newRules[index];
      newRules[index] = newRules[index + direction];
      newRules[index + direction] = temp;
      onChange(newRules);
    },
    [rules, onChange]
  );

  return (
    <div style={{ marginTop: 15, marginBottom: 15 }}>
      <FormLabel size={sizes.SMALL}>Priority Release Rules</FormLabel>
      {rules.map((rule, ruleIndex) => (
        <div
          key={ruleIndex}
          style={{
            border: '1px solid var(--border-color)',
            borderRadius: 4,
            padding: 10,
            marginBottom: 10,
            backgroundColor: 'var(--background-color)',
          }}
        >
          <div
            style={{
              display: 'flex',
              justifyContent: 'space-between',
              marginBottom: 10,
            }}
          >
            <div style={{ display: 'flex', alignItems: 'center' }}>
              <div
                style={{
                  display: 'flex',
                  flexDirection: 'column',
                  marginRight: 10,
                }}
              >
                <Button
                  kind={kinds.DEFAULT}
                  title="Move Up"
                  isDisabled={ruleIndex === 0}
                  onPress={() => handleMoveRule(ruleIndex, -1)}
                >
                  <Icon name={icons.COLLAPSE} />
                </Button>
                <Button
                  kind={kinds.DEFAULT}
                  title="Move Down"
                  isDisabled={ruleIndex === rules.length - 1}
                  onPress={() => handleMoveRule(ruleIndex, 1)}
                >
                  <Icon name={icons.EXPAND} />
                </Button>
              </div>
              <input
                type="text"
                value={rule.name || ''}
                style={{
                  background: 'transparent',
                  border: 'none',
                  color: 'inherit',
                  fontWeight: 'bold',
                  fontSize: 16,
                  outline: 'none',
                }}
                placeholder={`Rule ${ruleIndex + 1}`}
                onChange={(e) =>
                  handleUpdateRule(ruleIndex, { ...rule, name: e.target.value })
                }
              />
            </div>
            <Button
              kind={kinds.DANGER}
              title="Remove Rule"
              onPress={() => handleRemoveRule(ruleIndex)}
            >
              <Icon name={icons.DELETE} />
            </Button>
          </div>

          <div>
            {rule.conditions.map((condition, condIndex) => (
              <div
                key={condIndex}
                style={{
                  display: 'flex',
                  alignItems: 'center',
                  marginBottom: 10,
                  gap: 10,
                }}
              >
                {condIndex === 0 ? (
                  <span style={{ width: 40, fontWeight: 'bold' }}>IF</span>
                ) : (
                  <span style={{ width: 40, fontWeight: 'bold' }}>AND</span>
                )}

                <div style={{ width: 150 }}>
                  <FormInputGroup
                    type={inputTypes.SELECT}
                    name="conditionType"
                    value={condition.conditionType}
                    values={CONDITION_TYPES}
                    onChange={(change) => {
                      const newConds = [...rule.conditions];
                      newConds[condIndex] = {
                        ...condition,
                        conditionType: change.value as number,
                      };
                      handleUpdateRule(ruleIndex, {
                        ...rule,
                        conditions: newConds,
                      });
                    }}
                  />
                </div>

                <div style={{ width: 120 }}>
                  <FormInputGroup
                    type={inputTypes.SELECT}
                    name="operator"
                    value={condition.operator}
                    values={OPERATORS}
                    onChange={(change) => {
                      const newConds = [...rule.conditions];
                      newConds[condIndex] = {
                        ...condition,
                        operator: change.value as number,
                      };
                      handleUpdateRule(ruleIndex, {
                        ...rule,
                        conditions: newConds,
                      });
                    }}
                  />
                </div>

                {condition.conditionType ===
                ReleaseRuleConditionType.AudioType ? (
                  <div style={{ flex: 1 }}>
                    <FormInputGroup
                      type={inputTypes.SELECT}
                      name="value"
                      value={condition.value || 'Any'}
                      values={AUDIO_OPTIONS}
                      onChange={(change) => {
                        const newConds = [...rule.conditions];
                        newConds[condIndex] = {
                          ...condition,
                          value: change.value as string,
                        };
                        handleUpdateRule(ruleIndex, {
                          ...rule,
                          conditions: newConds,
                        });
                      }}
                    />
                  </div>
                ) : condition.conditionType ===
                  ReleaseRuleConditionType.CustomFormat ? (
                  <div style={{ flex: 1 }}>
                    <FormInputGroup
                      type={inputTypes.SELECT}
                      name="value"
                      value={
                        condition.value ||
                        (customFormatOptions.length > 0
                          ? customFormatOptions[0].key
                          : '')
                      }
                      values={customFormatOptions}
                      onChange={(change) => {
                        const newConds = [...rule.conditions];
                        newConds[condIndex] = {
                          ...condition,
                          value: change.value as string,
                        };
                        handleUpdateRule(ruleIndex, {
                          ...rule,
                          conditions: newConds,
                        });
                      }}
                    />
                  </div>
                ) : (
                  <div style={{ flex: 1 }}>
                    <FormInputGroup
                      type={inputTypes.TEXT}
                      name="value"
                      value={condition.value || ''}
                      placeholder="Enter value..."
                      onChange={(change) => {
                        const newConds = [...rule.conditions];
                        newConds[condIndex] = {
                          ...condition,
                          value: change.value as string,
                        };
                        handleUpdateRule(ruleIndex, {
                          ...rule,
                          conditions: newConds,
                        });
                      }}
                    />
                  </div>
                )}

                <Button
                  kind={kinds.DEFAULT}
                  title="Remove Condition"
                  isDisabled={rule.conditions.length === 1}
                  onPress={() => {
                    const newConds = [...rule.conditions];
                    newConds.splice(condIndex, 1);
                    handleUpdateRule(ruleIndex, {
                      ...rule,
                      conditions: newConds,
                    });
                  }}
                >
                  <Icon name={icons.CLOSE} />
                </Button>
              </div>
            ))}

            <Button
              kind={kinds.DEFAULT}
              style={{ marginTop: 5 }}
              onPress={() => {
                const newConds = [...rule.conditions];
                newConds.push({
                  conditionType: ReleaseRuleConditionType.ReleaseGroup,
                  operator: ReleaseRuleConditionOperator.Exact,
                  value: '',
                });
                handleUpdateRule(ruleIndex, { ...rule, conditions: newConds });
              }}
            >
              <Icon name={icons.ADD} /> Add Condition
            </Button>
          </div>
        </div>
      ))}
      <Button kind={kinds.PRIMARY} onPress={handleAddRule}>
        <Icon name={icons.ADD} /> Add Rule
      </Button>
    </div>
  );
}
